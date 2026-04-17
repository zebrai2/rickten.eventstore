# Rickten.Reactor

**Events → Commands**: Projection-based reactive command execution

## Overview

Rickten.Reactor completes the Rickten triangle:
- **Aggregator**: Command → Events
- **Projector**: Events → Read Model  
- **Reactor**: Event + Projection → Commands

A **Reaction** uses a projection to identify which aggregate streams are affected by a trigger event, then executes commands against those streams through the existing aggregator pipeline.

## Key Concepts

### Reaction

A first-class domain component that:
1. Subscribes to specific event types (via `EventTypeFilter`)
2. Uses a projection to identify affected aggregate streams
3. Builds and executes commands against each affected stream
4. Maintains a checkpoint for at-least-once delivery

### Projection-Based Stream Selection

Unlike simple event-to-command transformations, reactions use projections to answer: **"Which aggregate streams are affected by this trigger event?"**

This enables:
- **One-to-many**: A single trigger event can command multiple aggregate streams
- **Query capability**: Use read model state to determine targets
- **Deterministic views**: Projection state represents history up to (and including) the trigger event

## Public API

### Base Class

```csharp
public abstract class Reaction<TView, TCommand>
{
    protected Reaction(ITypeMetadataRegistry registry);

    public string ReactionName { get; }
    public string[]? EventTypeFilter { get; }

    // Reaction owns its projection
    public abstract IProjection<TView> Projection { get; }

    // Select zero, one, or many affected streams
    protected abstract IEnumerable<StreamIdentifier> SelectStreams(
        TView view, 
        StreamEvent trigger);

    // Build command for a specific target stream
    protected abstract TCommand BuildCommand(
        StreamIdentifier stream,
        TView view,
        StreamEvent trigger);
}
```

**Constructor requirement**: Reactions must be constructed with an `ITypeMetadataRegistry` instance. This:
- ✅ Validates the reaction is properly registered
- ✅ Loads metadata from the centralized registry
- ✅ Ensures the `[Reaction]` attribute is present
- ✅ Follows the same pattern as `StateFolder<TState>`

### Attribute

```csharp
[Reaction("MembershipDefinitionChanged", 
    EventTypes = ["MembershipDefinition.Changed.v1"])]
```

**TypeMetadataRegistry Integration:**

`ReactionAttribute` implements `ITypeMetadata`, providing:
- ✅ **Automatic registration** when assemblies are scanned
- ✅ **Duplicate detection** - throws if two reactions produce the same wire name
- ✅ **Discovery** - enumerate all reactions via the registry
- ✅ **Tooling support** - future CLI/diagnostics can query reaction metadata

Wire name format: `Reaction.{Name}.{ClassName}`

This ensures uniqueness even when multiple reactions share a logical name.

### Runner

```csharp
public static class ReactionRunner
{
    public static Task<long> CatchUpAsync<TState, TView, TCommand>(
        IEventStore eventStore,
        IReactionStore reactionStore,
        Reaction<TView, TCommand> reaction,
        IStateFolder<TState> folder,
        ICommandDecider<TState, TCommand> decider,
        ISnapshotStore? snapshotStore = null,
        string? reactionName = null,
        ILogger? logger = null,  // Optional logging
        CancellationToken cancellationToken = default);
}
```

**Optional logging**: Pass an `ILogger` to receive diagnostic information:
- ⚠️ **Warning**: When projection is ahead of reaction (indicates failure/recovery scenario)
- ℹ️ **Info**: When projection is rebuilt for historical accuracy

### Store

```csharp
public interface IReactionStore
{
    // Reaction checkpoint (last trigger event processed)
    Task<long> LoadReactionAsync(string reactionKey, ...);
    Task SaveReactionAsync(string reactionKey, long globalPosition, ...);

    // Reaction's private projection state
    Task<Projection<TView>?> LoadReactionProjectionAsync<TView>(string reactionKey, ...);
    Task SaveReactionProjectionAsync<TView>(string reactionKey, long globalPosition, TView state, ...);
}
```

## Example

```csharp
// Projection view: map definition IDs to affected memberships
public record MembershipDefinitionView(
    Dictionary<string, List<string>> DefinitionToMemberships);

// Projection: builds the mapping
[Projection("MembershipDefinitionIndex")]
public class MembershipDefinitionProjection 
    : Projection<MembershipDefinitionView>
{
    public override MembershipDefinitionView InitialView() => 
        new(new Dictionary<string, List<string>>());

    protected override MembershipDefinitionView ApplyEvent(
        MembershipDefinitionView view, 
        StreamEvent streamEvent)
    {
        return streamEvent.Event switch
        {
            MembershipDefinitionChangedEvent evt => 
                AddDefinition(view, evt.DefinitionId),
            UserRegisteredEvent evt => 
                AddMembershipToDefinition(view, evt.MembershipDefinitionId, evt.UserId),
            _ => view
        };
    }

    // ... helper methods
}

// Reaction: when definition changes, recalculate affected memberships
[Reaction("MembershipDefinitionChanged",
    EventTypes = ["MembershipDefinition.Changed.v1"])]
public sealed class MembershipDefinitionChangedReaction 
    : Reaction<MembershipDefinitionView, RecalculateMembershipCommand>
{
    private readonly MembershipDefinitionProjection _projection = new();

    // Constructor requires TypeMetadataRegistry
    public MembershipDefinitionChangedReaction(ITypeMetadataRegistry registry) 
        : base(registry) { }

    public override IProjection<MembershipDefinitionView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(
        MembershipDefinitionView view, 
        StreamEvent trigger)
    {
        var evt = (MembershipDefinitionChangedEvent)trigger.Event;

        // Query the projection view to find affected memberships
        if (view.DefinitionToMemberships.TryGetValue(evt.DefinitionId, out var membershipIds))
        {
            foreach (var membershipId in membershipIds)
            {
                yield return new StreamIdentifier("Membership", membershipId);
            }
        }
    }

    protected override RecalculateMembershipCommand BuildCommand(
        StreamIdentifier stream,
        MembershipDefinitionView view,
        StreamEvent trigger)
    {
        var evt = (MembershipDefinitionChangedEvent)trigger.Event;
        return new RecalculateMembershipCommand(
            stream.Identifier,
            $"Definition changed: {evt.Name}");
    }
}
```

## Execution Model

`ReactionRunner.CatchUpAsync` behavior:

1. **Load checkpoints** from `IReactionStore`:
   - Reaction checkpoint: last trigger event successfully processed
   - Projection checkpoint: last event applied to reaction's private projection
2. **Read events** from `IEventStore.LoadAllAsync`:
   - Start from `Min(reactionPosition, projectionPosition)`
   - Use projection's aggregate/event type filters for efficiency
3. **For each event**:
   - **Always** apply event to reaction's private projection if newer than projection checkpoint
   - **If event is a trigger** (matches `EventTypeFilter` and newer than reaction checkpoint):
     - Call `reaction.SelectStreams(projectionView, trigger)` to get target streams
     - For each selected stream:
       - Call `reaction.BuildCommand(stream, projectionView, trigger)`
       - Execute command through `StateRunner.ExecuteAsync`
     - **After all commands succeed**, save both checkpoints
4. **Return** final processed position

### Reaction-Owned Projection State

**Key insight**: Each reaction maintains its **own private projection state**, completely separate from any public projection stores.

- **Public projections**: Users run `ProjectionRunner.CatchUpAsync(eventStore, publicProjectionStore, projection, ...)`
- **Reaction projections**: The reaction uses the same `IProjection<TView>` but stores state in `IReactionStore` (reaction-private)

**Benefits:**
- ✅ No API pollution - `ProjectionRunner` stays clean
- ✅ No dangerous methods to misuse
- ✅ Performance - projection filters work normally
- ✅ Independence - public and reaction projections have separate lifecycles
- ✅ Simplicity - projection state is managed transparently by the runner

**The projection view always represents deterministic state** because:
1. It's caught up incrementally as events stream in
2. When a trigger event is processed, the projection already reflects all prior events
3. The view naturally represents state "up to and including" the trigger event

## Checkpoint Semantics

**Two checkpoints per reaction:**

1. **Reaction checkpoint**: Last trigger event successfully processed
   - Advances only after **all** commands for a trigger succeed
   - If any command fails, reaction checkpoint is **not** saved

2. **Projection checkpoint**: Last event applied to reaction's private projection
   - Advances as events are applied to the projection
   - Saved when reaction checkpoint is saved (kept in sync)
   - Enables efficient catch-up (don't rebuild projection from scratch)

**Guarantees:**
- At-least-once delivery (trigger events may be reprocessed after failure)
- Commands **must be idempotent** (replay may occur after partial execution)
- Projection state is always consistent with or ahead of reaction progress

## Design Boundaries

### Included
- `Reaction<TView, TCommand>` base class
- `[Reaction]` attribute
- `IReactionStore` interface (manages both reaction and projection checkpoints)
- `ReactionRunner` static runner
- Projection-based stream selection
- Reaction-owned projection state (private to each reaction)

### Not Included (Out of Scope for 1.1)
- Hosted services / daemons
- Polling loops
- Retry / backoff policy
- Leader election
- Distributed locks
- Exactly-once execution
- Transactional checkpoint + command append
- Multiple commands per stream
- Multiple aggregate types per reaction
- Stateful sagas / process managers
- Scheduled reactions / timers
- External side effects (HTTP, email, etc.)
- Automatic replay / reset tooling
- Reaction versioning

## Relationship to Other Patterns

| Pattern | Purpose | Mutates State | Appends Events |
|---------|---------|---------------|----------------|
| **Projection** | Events → Read Model | Yes (read model) | No |
| **Reaction** | Event + Projection → Commands | No (delegates to aggregator) | No (delegates to aggregator) |
| **Saga/Process Manager** | Long-running workflow | Yes (saga state) | Maybe |

Reactions:
- **Do not** mutate state directly
- **Do not** append events directly  
- **Delegate** state mutation and event appending to the aggregator command pipeline

This keeps reactions simple and composable.

## Philosophy

Rickten.Reactor follows Rickten's design principles:
- **Small primitives**: Base class, attribute, runner, store
- **Explicit dependencies**: Reaction owns its projection
- **Static runners**: No dependency injection required
- **Explicit persistence**: `IReactionStore` interface
- **No hosting concerns**: Mechanism, not framework

A future hosting/daemon project may call `ReactionRunner.CatchUpAsync` repeatedly, but that is out of scope for Rickten.Reactor itself.

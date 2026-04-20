# Rickten.Reactor

**Events → Commands**: Projection-based reactive command execution

## Overview

Rickten.Reactor completes the Rickten triangle:
- **Aggregator**: Command → Events
- **Projector**: Events → Read Model  
- **Reactor**: Event + Projection → Commands

A **Reaction** uses a projection to identify which aggregate streams are affected by a trigger event, then executes commands against those streams through the existing aggregator pipeline.

## Installation

### NuGet Package

```bash
dotnet add package Rickten.Reactor --version 1.1.0
```

### Dependency Injection Setup

Rickten.Reactor provides first-class DI registration support with assembly scanning:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Rickten.Reactor;

// 1. Register Event Store with type metadata (required for reactions)
services.AddEventStore(
    options => options.UseSqlServer(connectionString),
    typeof(MyEvent).Assembly,
    typeof(MyReaction).Assembly);  // Include assemblies containing reactions

// 2. Register all reactions from assemblies
services.AddReactions(typeof(MyReaction).Assembly);

// Or use marker types (recommended)
services.AddReactions<MyReaction>();

// Register reactions from multiple assemblies
services.AddReactions<Reaction1, Reaction2>();
services.AddReactions<Reaction1, Reaction2, Reaction3>();
```

**Important**: Reactions require `ITypeMetadataRegistry` for validation during construction. The same assemblies containing reactions must be registered with both `AddEventStore(...)` and `AddReactions(...)`.

#### What Gets Registered

`AddReactions` scans assemblies for:
- Concrete, non-abstract classes
- Decorated with `[Reaction]` attribute
- Inheriting from `Reaction<TView, TCommand>`

Each reaction type is registered as:
1. **Concrete type** (transient) - for direct resolution
2. **Closed base type** `Reaction<TView, TCommand>` (transient, enumerable) - for resolving all reactions of a specific type

This enables both direct injection and collection injection:

```csharp
public class MyService
{
    // Direct injection
    public MyService(MembershipReaction reaction) { }

    // Collection injection - all reactions with same signature
    public MyService(IEnumerable<Reaction<MembershipView, RecalculateCommand>> reactions) { }
}
```

#### Safe Repeated Registration

`AddReactions(...)` can be safely called multiple times with overlapping assemblies - duplicate registrations are automatically prevented using `TryAddTransient` and `TryAddEnumerable`.

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

`ReactionRunner` is an instance-based service that orchestrates reaction execution:

```csharp
public sealed class ReactionRunner
{
    public ReactionRunner(
        IEventStore eventStore,
        IProjectionStore projectionStore,
        IReactionRepository reactionRepository,
        ILogger<ReactionRunner>? logger = null);

    public Task<long> CatchUpAsync<TState, TView, TCommand>(
        Reaction<TView, TCommand> reaction,
        AggregateCommandExecutor<TState, TCommand> executor,
        CancellationToken cancellationToken = default);
}
```

**Automatic logging**: The runner accepts an `ILogger<ReactionRunner>` in its constructor for diagnostic information:
- ⚠️ **Warning**: When projection is ahead of reaction (indicates failure/recovery scenario)
- 🔴 **Critical**: When event metadata is missing (CorrelationId or EventId)

**Dependency Injection**: `ReactionRunner` is automatically registered as a singleton when calling `AddReactions`:

```csharp
services.AddReactions<MyReaction>();

// ReactionRunner is now available for injection
public class MyService
{
    private readonly ReactionRunner _runner;

    public MyService(ReactionRunner runner)
    {
        _runner = runner;
    }
}
```

### Checkpoint Storage

Reactions use two separate storage mechanisms:

1. **`IReactionRepository`** - Stores execution checkpoints:
   - Reaction name
   - Trigger position (last processed trigger event)
   - Projection position (how far projection was caught up)

2. **`IProjectionStore`** (with `"reaction"` namespace) - Stores projection view state:
   - Reaction name as key
   - Materialized projection view
   - Global position

This separation enables:
- ✅ Independent checkpoint management
- ✅ Efficient projection catch-up on drift
- ✅ Shared infrastructure with public projections (different namespace)

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

`ReactionRunner.CatchUpAsync` orchestrates the following:

1. **Load checkpoints**:
   - Reaction checkpoint from `IReactionRepository` (trigger and projection positions)
   - Projection view from `IProjectionStore` (using "reaction" namespace)
2. **Synchronize projection** to reaction position if drift detected:
   - If projection ahead: Rebuild from scratch to reaction position
   - If projection behind: Catch up from checkpoint to reaction position
   - Uses `ProjectionRunner.SyncToPositionAsync` internally
3. **Process new events** from `IEventStore.LoadAllAsync`:
   - Merge projection events and trigger events by global position
   - Apply projection events to update view incrementally
4. **For each trigger event**:
   - Call `reaction.SelectStreams(projectionView, trigger)` to get target streams
   - For each selected stream:
     - Call `reaction.BuildCommand(stream, projectionView, trigger)`
     - Execute command through `AggregateCommandExecutor`
   - Save reaction checkpoint after all commands succeed
5. **Save final checkpoint** with updated positions

### Reaction-Owned Projection State

**Key insight**: Each reaction maintains its **own private projection state**, stored separately from the reaction checkpoint.

- **`IReactionRepository`**: Stores execution state (trigger position, projection position)
- **`IProjectionStore`** (reaction namespace): Stores the materialized projection view
- **Public projections**: Use `ProjectionRunner.CatchUpAsync` with "system" namespace
- **Reaction projections**: Same `IProjection<TView>` interface but stored in "reaction" namespace

**Benefits:**
- ✅ Clean separation - checkpoint positions vs view state
- ✅ Performance - projection filters work normally
- ✅ Independence - public and reaction projections have separate lifecycles
- ✅ Simplicity - projection state managed transparently by the runner
- ✅ Shared infrastructure - same database, different namespace

**The projection view always represents deterministic state** because:
1. It's synchronized to the reaction position before processing begins
2. When a trigger event is processed, the projection reflects all prior events
3. The view represents state "up to and including" the trigger event

## Checkpoint Semantics

**Separate checkpoint storage:**

1. **Reaction checkpoint** (in `IReactionRepository`):
   - Stores: Reaction name, trigger position, projection position
   - Saved after **all** commands for a trigger succeed
   - If any command fails, checkpoint is **not** saved

2. **Projection view** (in `IProjectionStore` with "reaction" namespace):
   - Stores: Materialized projection state at a specific position
   - Saved during sync step by `ProjectionRunner.SyncToPositionAsync`
   - Not saved after each trigger (sync handles it on restart if needed)

**Guarantees:**
- At-least-once delivery (trigger events may be reprocessed after failure)
- Commands **must be idempotent** (replay may occur after partial execution)
- Projection state can be behind reaction checkpoint (sync catches it up on startup)

## At-Least-Once Delivery and Idempotency

**⚠️ Critical: Commands produced by reactions may be executed multiple times.**

Reactions provide **at-least-once delivery** semantics:
- If a reaction crashes after executing some commands but before saving its checkpoint, those commands will be re-executed on restart
- If a command execution fails partway through a batch, the entire batch may be retried
- Network failures, process restarts, or exceptions can all trigger re-execution

### Required: Idempotent Command Design

**All commands executed by reactions MUST be idempotent.** This means:

✅ **Safe to execute multiple times with the same result**
```csharp
// Good: Set absolute state
new UpdateUserStatus(userId, status: "Active");

// Good: Use conditional logic in decider
if (state.Status != "Active") {
    yield return new UserStatusChangedEvent(userId, "Active");
}
```

❌ **NOT safe if dependent on execution count**
```csharp
// Bad: Increment counter (executes twice = wrong count)
new IncrementCounter(userId);

// Bad: Add to collection without deduplication
new AddItem(userId, item);
```

### Design Strategies

1. **Use absolute state commands** - Set values rather than apply deltas
2. **Check current state in deciders** - Only emit events if state actually changes
3. **Use correlation/causation IDs** - Detect and skip duplicate executions
4. **Design aggregates for idempotency** - Make state transitions naturally idempotent

### Example: Idempotent Recalculation

```csharp
// Command is safe to execute multiple times
protected override RecalculateMembershipCommand BuildCommand(
    StreamIdentifier stream,
    MembershipDefinitionView view,
    StreamEvent trigger)
{
    return new RecalculateMembershipCommand(
        stream.Identifier,
        definitionVersion: ((MembershipDefinitionChangedEvent)trigger.Event).Version);
}

// Decider checks version before emitting events
public IEnumerable<object> Decide(MembershipState state, RecalculateMembershipCommand command)
{
    // Only recalculate if definition version has actually changed
    if (state.DefinitionVersion == command.DefinitionVersion)
    {
        yield break; // Already processed this version
    }

    yield return new MembershipRecalculatedEvent(
        state.MembershipId,
        command.DefinitionVersion,
        newValue: CalculateValue(state, command));
}
```

### Checkpoint Recovery

On startup after a crash:
1. **Sync step** detects if projection is behind reaction checkpoint
2. **Projection is caught up** to reaction position using `ProjectionRunner`
3. **Reaction resumes** from last saved checkpoint
4. **Unconfirmed triggers are reprocessed** - commands may execute again

This is why idempotency is non-negotiable.

### Projection Checkpoint Frequency

**Projection state is only saved during the sync step, not after processing each trigger.**

If a reaction crashes mid-processing:
- Reaction checkpoint remains at the last successfully processed trigger
- Projection checkpoint may be behind (not saved yet)
- On restart, the sync step catches up the projection before processing resumes

**Performance consideration**: The sync step rebuilds or catches up the projection to match the reaction checkpoint. For reactions that process many projection events between triggers:

💡 **Recommendation**: Configure your `IProjectionStore` implementation with a **batch size of 1** for reaction projections (those in the "reaction" namespace). This ensures projection checkpoints are saved frequently during the sync step, minimizing the amount of work needed on recovery.

Example with Entity Framework implementation:
```csharp
services.AddProjectionStore(options => 
{
    options.BatchSize = 1; // Save after every event during sync
});
```

This trades a small amount of write throughput for faster crash recovery and more granular checkpointing.

## Metadata Propagation

When `ReactionRunner` executes commands in response to trigger events, it automatically adds metadata to the resulting events to maintain traceability:

### Automatically Added Metadata

1. **CorrelationId** - Propagated from trigger event (if present)
   - Tracks related events across aggregate boundaries
   - Source: `Client` (even if trigger was `System`)

2. **CausationId** - Set to trigger event's system `EventId`
   - References the event that caused this reaction
   - Source: `Client`

3. **ReactionWireName** - Identifies the reaction that produced the event
   - Format: `Reaction.{Name}.{ClassName}`
   - Example: `"Reaction.MembershipDefinitionChanged.MembershipDefinitionChangedReaction"`
   - Source: `Client`

### Reading Reaction Metadata

Use the typed extension methods from `EventMetadataExtensions`:

```csharp
// Get the reaction that produced this event
var reactionWireName = streamEvent.Metadata.GetReactionWireName();
// Returns: "Reaction.MembershipDefinitionChanged.MembershipDefinitionChangedReaction"

// Get the trigger event that caused this
var causationId = streamEvent.Metadata.GetCausationId();

// Get the correlation ID for distributed tracing
var correlationId = streamEvent.Metadata.GetCorrelationId();
```

This enables:
- **Debugging**: Trace which reaction produced specific events
- **Monitoring**: Track reaction execution patterns
- **Auditing**: Full causation chain from trigger to result

## Design Boundaries

### Included
- `Reaction<TView, TCommand>` base class
- `[Reaction]` attribute with `ITypeMetadata` integration
- `ReactionRunner` static runner
- `ServiceCollectionExtensions` for DI registration
- Projection-based stream selection
- Reaction-owned projection state (using `IProjectionStore` with "reaction" namespace)
- TypeMetadataRegistry integration
- Dual-checkpoint model with historical projection guarantee

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
- **Small primitives**: Base class, attribute, runner service, repositories
- **Explicit dependencies**: Reaction owns its projection
- **Instance-based runner**: `ReactionRunner` is a DI-registered service with injected dependencies
- **Separate checkpoint concerns**: `IReactionRepository` for positions, `IProjectionStore` for views
- **Explicit persistence**: Clear interfaces with namespace separation
- **No hosting concerns**: Mechanism, not framework

`Rickten.Runtime` provides hosted services that call `ReactionRunner.CatchUpAsync` repeatedly in polling loops.

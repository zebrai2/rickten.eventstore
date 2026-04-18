# Rickten.Aggregator

A lightweight library for implementing event-sourced aggregates with a clean separation between state folding and command decision-making. Features strict-by-default validation with `[Aggregate]` and `[Command]` attributes.

## Core Concepts

### IStateFolder<TState>

Folds events into aggregate state:

```csharp
public interface IStateFolder<TState>
{
    TState InitialState();
    TState Apply(TState state, object @event);
}
```

### ICommandDecider<TState, TCommand>

Decides what events should occur based on command + state:

```csharp
public interface ICommandDecider<TState, TCommand>
{
    IReadOnlyList<object> Execute(TState state, TCommand command);
}
```

### Base Classes (Recommended)

**StateFolder<TState>** - Abstract base class that:
- **Requires `[Aggregate]` attribute on state type TState** for validation
- **Requires `ITypeMetadataRegistry` constructor parameter** for event type discovery and validation
- **Uses explicit handler methods**: Define `protected TState When(EventType e, TState state)` for each event
- **Validates event coverage by default**: Ensures all events have handlers (opt-out with `ValidateEventCoverage = false`)
- Handles null event checks automatically
- Offers `EnsureValid(condition, message)` helper for state validation
- Provides `IgnoredEvents` property to exclude specific events from validation

**CommandDecider<TState, TCommand>** - Abstract base class that:
- **Requires `[Aggregate]` attribute on state type TState** for validation
- Validates commands belong to the aggregate (via `[Command]` attribute)
- Validates produced events match the aggregate
- Separates validation from event production
- Provides helper methods: `Event()`, `NoEvents()`, `Events()`, `Require()`, `RequireEqual()`, `RequireNotNull()`
- Provides `CreateStreamId(identifier)` helper for stream identifier creation
- Clean `ValidateCommand()` and `ExecuteCommand()` methods to override

### StateRunner

Utilities for loading state and executing commands:

```csharp
public static class StateRunner
{
    // Load events from stream, validate ordering/completeness, fold into state
    Task<(TState State, long Version)> LoadStateAsync<TState>(...);

    // Execute command against latest state or expected version (based on command's ExpectedVersionKey)
    Task<(TState State, long Version, IReadOnlyList<StreamEvent> Events)> ExecuteAsync<TState, TCommand>(...);
}
```

## Attributes

### [Aggregate]

Required on the state type used by `StateFolder` and `CommandDecider`:

```csharp
[Aggregate("SessionReview")]
public sealed record SessionReviewState
{
    public string SessionId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    // ...
}

public sealed class SessionReviewStateFolder : StateFolder<SessionReviewState>
{
    // Uses the [Aggregate] attribute from SessionReviewState
}

public sealed class SessionReviewCommandDecider : CommandDecider<SessionReviewState, SessionReviewCommand>
{
    // Uses the [Aggregate] attribute from SessionReviewState
}
```

Properties:
- `Name` (required) - The aggregate name
- `ValidateEventCoverage` (optional, default `true`) - Enable/disable event coverage validation
- `SnapshotInterval` (optional, default `0`) - Automatic snapshot interval (0 = disabled)

**Automatic Snapshots:**

Configure automatic snapshots by setting the `SnapshotInterval` on the state type:

```csharp
[Aggregate("SessionReview", SnapshotInterval = 50)]
public sealed record SessionReviewState
{
    // Automatically snapshots every 50 events when using StateRunner
    // ...
}
```

When `SnapshotInterval` > 0:
- `StateRunner.ExecuteAsync` automatically saves snapshots at the configured interval when you provide a `snapshotStore`
- `StateRunner.LoadStateAsync` automatically loads from the latest snapshot when you provide a `snapshotStore`, reducing event replay

This provides a complete optimization path for aggregates with long event histories.

### [Command]

**Required** for commands executed through StateRunner:

```csharp
[Command("SessionReview", Name = "Start Session", Description = "Starts a new review session")]
public sealed record StartSession(string SessionId, string UserId) : SessionReviewCommand;

[Command("Order", ExpectedVersionKey = "ExpectedVersion")]
public sealed record ApproveOrder(string OrderId);
```

Properties:
- `Aggregate` (required) - Which aggregate the command belongs to
- `Name` (optional) - Human-readable command name
- `Description` (optional) - What the command does
- `ExpectedVersionKey` (optional) - Metadata key for expected stream version

## Expected Stream Version Support

Commands can execute against either the latest aggregate state (default) or a caller-provided expected version for CQRS stale-read protection.

### Default Behavior (Latest Version)

By default, commands execute against the current aggregate stream version at execution time. This is suitable for most commands, including Reactor side effects.

```csharp
[Command("Order")]
public sealed record CancelOrder(string OrderId);

var registry = scope.ServiceProvider.GetRequiredService<ITypeMetadataRegistry>();

// Execute against latest state
await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    new CancelOrder("order-1"),
    registry);
```

### Expected Version (Metadata-Based)

For CQRS command handling where the user's decision was based on a specific read model version, set `ExpectedVersionKey` on the `[Command]` attribute. The expected version is provided as metadata when executing the command.

```csharp
[Command("Order", ExpectedVersionKey = "ExpectedVersion")]
public sealed record ApproveOrder(string OrderId);

var registry = scope.ServiceProvider.GetRequiredService<ITypeMetadataRegistry>();

// User observed version 5 from read model
var order = await readModel.GetOrder("order-1"); // returns version 5

// Command will only execute if stream is still at version 5
var result = await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    new ApproveOrder("order-1"),
    registry,
    metadata: [
        new AppendMetadata("ExpectedVersion", order.Version),
        new AppendMetadata("CorrelationId", correlationId)
    ]);
```

**How it works:**

1. Command declares `ExpectedVersionKey = "ExpectedVersion"` in its `[Command]` attribute
2. Caller passes the expected version in metadata using the same key
3. `StateRunner` extracts the expected version from metadata
4. If stream version doesn't match, `StreamVersionConflictException` is thrown **before** the command decider runs
5. The expected version metadata is **not persisted** with events

**Benefits:**

- Expected version is request context, not command data
- Commands remain simple and focused on business intent
- The command payload does not carry expected version; callers provide it as metadata when executing that command
- Expected version metadata is consumed by StateRunner and not persisted with events

### Expected Version Behavior

| Scenario | Behavior |
|----------|----------|
| Command without `ExpectedVersionKey` | Executes against latest state |
| Command with `ExpectedVersionKey` + matching metadata | Validates version matches before execution |
| Command with `ExpectedVersionKey` but missing metadata | Throws `InvalidOperationException` |
| Command with `ExpectedVersionKey` but invalid metadata value | Throws `InvalidOperationException` |
| Version mismatch | Throws `StreamVersionConflictException` before deciding |
| Idempotent command at expected version | Returns success with no events |
| New stream (version 0) | Works correctly with expected version 0 |

**Metadata Value Conversion:**

The expected version metadata value is automatically converted from:
- `long` - used directly
- `int`, `short`, `byte` - converted to long
- `string` - parsed as long if valid

**Important:** Version validation happens **before** the command decider runs. If the stream version doesn't match, the command is never executed because the user's decision was based on stale state.

## Quick Start

### 1. Define Your Commands

```csharp
public abstract record SessionReviewCommand
{
    [Command("SessionReview")]
    public sealed record StartSession(string SessionId, string UserId) : SessionReviewCommand;

    [Command("SessionReview")]
    public sealed record RecordInteraction(string InteractionId, string Type, string Content) : SessionReviewCommand;

    [Command("SessionReview")]
    public sealed record ProvideFeedback(int Rating, string? Comment) : SessionReviewCommand;
}
```

### 2. Define Your Events

```csharp
public abstract record SessionReviewEvent
{
    [Event("SessionReview", "SessionStarted", 1)]
    public sealed record SessionStarted(string SessionId, string UserId, DateTime StartedAt) : SessionReviewEvent;

    [Event("SessionReview", "InteractionRecorded", 1)]
    public sealed record InteractionRecorded(string InteractionId, string Type, string Content, DateTime RecordedAt) : SessionReviewEvent;
}
```

### 3. Define Your State

```csharp
[Aggregate("SessionReview")]
public sealed record SessionReviewState
{
    public string SessionId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTime? StartedAt { get; init; }
    public ImmutableList<SessionReviewEvent.InteractionRecorded> Interactions { get; init; } = ImmutableList<SessionReviewEvent.InteractionRecorded>.Empty;
}
```

### 4. Define Your State Folder

```csharp
using Rickten.EventStore;
using Rickten.Aggregator;

public sealed class SessionReviewStateFolder : StateFolder<SessionReviewState>
{
    // Inject the type metadata registry
    public SessionReviewStateFolder(ITypeMetadataRegistry registry) : base(registry) { }

    public override SessionReviewState InitialState() => new();

    protected SessionReviewState When(SessionStarted e, SessionReviewState state) =>
        state with { SessionId = e.SessionId, UserId = e.UserId, StartedAt = e.StartedAt };

    protected SessionReviewState When(InteractionRecorded e, SessionReviewState state) =>
        state with { Interactions = state.Interactions.Add(e) };
}
```

### 5. Implement Command Decider (Using Base Class with Helpers)

```csharp
[Aggregate("SessionReview")]
public sealed class SessionReviewCommandDecider : CommandDecider<SessionReviewState, SessionReviewCommand>
{
    // Validation happens BEFORE execution - keeps decision logic clean
    protected override void ValidateCommand(SessionReviewState state, SessionReviewCommand command)
    {
        switch (command)
        {
            case SessionReviewCommand.RecordInteraction:
                RequireEqual(state.Status, SessionStatus.Active, "Cannot record interaction - session is not active");
                break;

            case SessionReviewCommand.ProvideFeedback feedback:
                RequireEqual(state.Status, SessionStatus.Active, "Cannot provide feedback - session is not active");
                Require(feedback.Rating >= 1 && feedback.Rating <= 5, "Rating must be between 1 and 5");
                break;

            case SessionReviewCommand.CompleteSession:
                RequireEqual(state.Status, SessionStatus.Active, "Cannot complete session - session is not active");
                break;
        }
    }

    protected override IReadOnlyList<object> ExecuteCommand(SessionReviewState state, SessionReviewCommand command)
    {
        return command switch
        {
            SessionReviewCommand.StartSession start => ExecuteStartSession(state, start),
            SessionReviewCommand.RecordInteraction record => Event(
                new SessionReviewEvent.InteractionRecorded(
                    record.InteractionId, 
                    record.Type, 
                    record.Content, 
                    DateTime.UtcNow)),
            SessionReviewCommand.ProvideFeedback feedback => Event(
                new SessionReviewEvent.FeedbackProvided(
                    feedback.Rating, 
                    feedback.Comment, 
                    DateTime.UtcNow)),
            _ => throw new InvalidOperationException("Unknown command type")
        };
    }

    private IReadOnlyList<object> ExecuteStartSession(SessionReviewState state, SessionReviewCommand.StartSession command)
    {
        // Idempotent check
        if (state.Status != SessionStatus.NotStarted)
            return NoEvents();

        return Event(new SessionReviewEvent.SessionStarted(command.SessionId, command.UserId, DateTime.UtcNow));
    }
}
```

### 6. Usage

```csharp
using var scope = serviceProvider.CreateScope();

var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
var registry = scope.ServiceProvider.GetRequiredService<ITypeMetadataRegistry>();
var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>(); // optional
var folder = new SessionReviewStateFolder(registry);
var decider = new SessionReviewCommandDecider();

// Use the helper to create stream identifier
var streamId = decider.CreateStreamId("session-1");

// Execute a command (with automatic snapshots if configured)
var command = new SessionReviewCommand.StartSession("session-1", "user-123");
var (newState, newVersion, events) = await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    command,
    registry,
    snapshotStore); // Optional: enables automatic snapshots

Console.WriteLine($"New state: {newState.Status}, Version: {newVersion}");
Console.WriteLine($"Events appended: {events.Count}");
```

### 7. Automatic Snapshots

Configure snapshots declaratively with the `[Aggregate]` attribute:

```csharp
[Aggregate("SessionReview", SnapshotInterval = 50)]
public sealed class SessionReviewStateFolder : StateFolder<SessionReviewState>
{
    // ...
}

// In your code
using var scope = serviceProvider.CreateScope();
var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
var registry = scope.ServiceProvider.GetRequiredService<ITypeMetadataRegistry>();
var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();

var (state, version, events) = await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    command,
    registry,
    snapshotStore); // Snapshot saved automatically at versions 50, 100, 150, etc.
```

**Snapshot Behavior:**
- `SnapshotInterval = 0` (default) - No automatic snapshots
- `SnapshotInterval > 0` - Snapshots saved every N events
- Snapshots only saved when `snapshotStore` parameter is provided
- Idempotent commands (no events) don't trigger snapshots
- Exposed via `StateFolder<TState>.SnapshotInterval` property

## Design Principles

1. **Separation of Concerns**
   - `StateFolder<TState>`: Pure state transitions (events → state)
   - `CommandDecider<TState, TCommand>`: Business logic split into:
     - `ValidateCommand`: Validate against current state
     - `ExecuteCommand`: Produce events (validation already done)
   - `StateRunner`: Orchestration (load, decide, append, fold)

2. **Simplicity**
   - Empty list = idempotent operation (use `NoEvents()` helper)
   - Single event = use `Event(@event)` helper
   - Multiple events = use `Events(event1, event2)` helper
   - Exception = business rule violation
   - Base classes handle common patterns

3. **Validation**
   - `[Aggregate]` attribute required on state types (not on folder or decider implementations)
   - Commands validated against aggregate (via `[Command]` attribute)
   - Events validated against aggregate (via `[Event]` attribute)
   - Event coverage validated at construction: all events must have `When()` handlers
   - Clear error messages guide developers

4. **Type Safety**
   - `StreamEvent` provides full event context in StateRunner (pointer, metadata)
   - `IStateFolder.Apply()` works with unwrapped event objects
   - `[Event]` and `[Command]` attributes for proper classification
   - Aggregate boundaries enforced at runtime

## Helper Methods

### CommandDecider<TState, TCommand> Helpers

**Event Production:**
- `NoEvents()` - Returns empty list for idempotent operations
- `Event(object @event)` - Returns single event
- `Events(params object[] events)` - Returns multiple events

**Validation:**
- `Require(bool condition, string message)` - Throws if condition is false
- `RequireEqual<T>(T actual, T expected, string message)` - Throws if values don't match
- `RequireNotNull<T>(T? value, string message)` - Throws if value is null

**Utilities:**
- `CreateStreamId(string identifier)` - Creates StreamIdentifier with aggregate name
- `AggregateName` property - Gets the aggregate name from `[Aggregate]` attribute

### StateFolder<TState> Helpers

**Validation:**
- `EnsureValid(bool condition, string message)` - Throws if state transition is invalid

**Properties:**
- `IgnoredEvents` - Override to list event types excluded from coverage validation
- `SnapshotInterval` - Gets the configured snapshot interval from `[Aggregate]` attribute

## Error Handling

- **Idempotent operations**: Return `NoEvents()`
- **Business rule violations**: Throw `InvalidOperationException` in `ValidateCommand` (or use helpers)
- **Concurrency conflicts**: Handled by `EventStore.AppendAsync`
- **Missing `[Aggregate]` attribute**: Throws on first instance creation with helpful message
- **Wrong aggregate**: Validates commands/events match the decider's aggregate

## Stream Validation

`StateRunner.LoadStateAsync` validates:
- ✅ Stream identifier consistency
- ✅ Sequential version numbering (1, 2, 3...)
- ✅ No gaps in versions
- ✅ No duplicate versions
- ✅ No null events

## Integration with Rickten.EventStore

`StateRunner.ExecuteAsync` handles:
1. Load current state from event store (with validation)
2. Execute command to get events
3. Append events with optimistic concurrency
4. Fold new events into state
5. Return updated state and version

## Examples

See `Rickten.Aggregator.Examples` namespace for a complete SessionReview implementation:
- `SessionReviewState` + `SessionReviewStateFolder : StateFolder<>`
- `SessionReviewCommand` + `SessionReviewCommandDecider : CommandDecider<>`
- `SessionReviewEvent` with `[Event]` attributes

Run tests in `Rickten.Aggregator.Tests` to see usage patterns.

## License

MIT

# Rickten.Aggregator

A lightweight library for implementing event-sourced aggregates with a clean separation between state folding and command decision-making. Features strict-by-default validation with `[Aggregate]` and `[Command]` attributes, and a DDD Repository pattern for aggregate persistence.

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

### IAggregateRepository<TState>

Manages the complete persistence lifecycle for aggregates following the DDD Repository pattern:

```csharp
public interface IAggregateRepository<TState>
{
    // Load state from events and snapshots
    Task<(TState State, long Version)> LoadStateAsync(
        StreamIdentifier streamIdentifier, 
        CancellationToken cancellationToken = default);

    // Persist events to the event store
    Task<IReadOnlyList<StreamEvent>> AppendEventsAsync(
        StreamPointer pointer, 
        IReadOnlyList<AppendEvent> events, 
        CancellationToken cancellationToken = default);

    // Apply events and save snapshot if at interval
    Task<TState> SaveSnapshotIfNeededAsync(
        TState state, 
        IReadOnlyList<StreamEvent> appendedEvents, 
        CancellationToken cancellationToken = default);
}
```

**Architecture Principle: Events First, State Second**
- Events are the source of truth and are persisted **first**
- State is derived from events and is folded **only when needed** (for snapshots)
- This ensures data safety: events are safely stored before any derived state computation

### AggregateCommandExecutor<TState, TCommand>

Orchestrates the command execution workflow:

```csharp
public class AggregateCommandExecutor<TState, TCommand>
{
    // Execute command: load state → decide → append events → fold + snapshot
    Task<(TState State, long Version, IReadOnlyList<StreamEvent> Events)> ExecuteAsync(
        StreamIdentifier streamIdentifier,
        TCommand command,
        IReadOnlyList<AppendMetadata>? metadata = null,
        CancellationToken cancellationToken = default);
}
```

**Workflow:**
1. Load current state from repository (events + snapshots)
2. Execute command via decider to produce events
3. Append events to event store (persist first)
4. Fold events and save snapshot if at interval (derive state second)

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
- Provides **protected** `CreateStreamId(identifier)` helper for stream identifier creation within decider implementations
- Clean `ValidateCommand()` and `ExecuteCommand()` methods to override

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
    // Automatically snapshots every 50 events when using AggregateCommandExecutor
    // ...
}
```

When `SnapshotInterval` > 0:
- `AggregateCommandExecutor.ExecuteAsync` automatically saves snapshots at the configured interval when a `snapshotStore` is registered
- `AggregateRepository.LoadStateAsync` automatically loads from the latest snapshot when a `snapshotStore` is registered, reducing event replay

This provides a complete optimization path for aggregates with long event histories.

### [Command]

**Required** for commands executed through AggregateCommandExecutor:

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

// Configure services
services.AddSingleton<IStateFolder<OrderState>, OrderStateFolder>();
services.AddSingleton<ICommandDecider<OrderState, OrderCommand>, OrderCommandDecider>();
services.AddTransient<IAggregateRepository<OrderState>, AggregateRepository<OrderState>>();
services.AddTransient<AggregateCommandExecutor<OrderState, OrderCommand>>();

// Execute against latest state
var executor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<OrderState, OrderCommand>>();
var streamId = new StreamIdentifier("Order", "order-1");

var (state, version, events) = await executor.ExecuteAsync(
    streamId,
    new CancelOrder("order-1"));
```

### Expected Version (Metadata-Based)

For CQRS command handling where the user's decision was based on a specific read model version, set `ExpectedVersionKey` on the `[Command]` attribute. The expected version is provided as metadata when executing the command.

```csharp
[Command("Order", ExpectedVersionKey = "ExpectedVersion")]
public sealed record ApproveOrder(string OrderId);

// User observed version 5 from read model
var order = await readModel.GetOrder("order-1"); // returns version 5

var executor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<OrderState, OrderCommand>>();
var streamId = new StreamIdentifier("Order", "order-1");

// Command will only execute if stream is still at version 5
var result = await executor.ExecuteAsync(
    streamId,
    new ApproveOrder("order-1"),
    metadata: [
        new AppendMetadata("ExpectedVersion", order.Version),
        new AppendMetadata("CorrelationId", correlationId)
    ]);
```

**How it works:**

1. Command declares `ExpectedVersionKey = "ExpectedVersion"` in its `[Command]` attribute
2. Caller passes the expected version in metadata using the same key
3. `AggregateCommandExecutor` extracts the expected version from metadata
4. If stream version doesn't match, `StreamVersionConflictException` is thrown **before** the command decider runs
5. The expected version metadata is **not persisted** with events

**Benefits:**

- Expected version is request context, not command data
- Commands remain simple and focused on business intent
- The command payload does not carry expected version; callers provide it as metadata when executing that command
- Expected version metadata is consumed by AggregateCommandExecutor and not persisted with events

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

    [Command("SessionReview")]
    public sealed record CompleteSession : SessionReviewCommand;
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

    [Event("SessionReview", "FeedbackProvided", 1)]
    public sealed record FeedbackProvided(int Rating, string? Comment, DateTime ProvidedAt) : SessionReviewEvent;

    [Event("SessionReview", "SessionCompleted", 1)]
    public sealed record SessionCompleted(DateTime CompletedAt) : SessionReviewEvent;
}
```

### 3. Define Your State

```csharp
public enum SessionStatus
{
    NotStarted,
    Active,
    Completed
}

[Aggregate("SessionReview", SnapshotInterval = 50)]
public sealed record SessionReviewState
{
    public string SessionId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public SessionStatus Status { get; init; } = SessionStatus.NotStarted;
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public ImmutableList<SessionReviewEvent.InteractionRecorded> Interactions { get; init; } = ImmutableList<SessionReviewEvent.InteractionRecorded>.Empty;
    public int? Rating { get; init; }
    public string? FeedbackComment { get; init; }
}
```

### 4. Define Your State Folder

```csharp
using Rickten.EventStore;
using Rickten.Aggregator;

public sealed class SessionReviewStateFolder : StateFolder<SessionReviewState>
{
    // Inject the type metadata registry - required by StateFolder<TState>
    public SessionReviewStateFolder(ITypeMetadataRegistry registry) : base(registry) { }

    public override SessionReviewState InitialState() => new();

    protected SessionReviewState When(SessionReviewEvent.SessionStarted e, SessionReviewState state) =>
        state with 
        { 
            SessionId = e.SessionId, 
            UserId = e.UserId, 
            StartedAt = e.StartedAt,
            Status = SessionStatus.Active
        };

    protected SessionReviewState When(SessionReviewEvent.InteractionRecorded e, SessionReviewState state) =>
        state with { Interactions = state.Interactions.Add(e) };

    protected SessionReviewState When(SessionReviewEvent.FeedbackProvided e, SessionReviewState state) =>
        state with 
        { 
            Rating = e.Rating, 
            FeedbackComment = e.Comment 
        };

    protected SessionReviewState When(SessionReviewEvent.SessionCompleted e, SessionReviewState state) =>
        state with 
        { 
            Status = SessionStatus.Completed,
            CompletedAt = e.CompletedAt
        };
}
```

### 5. Implement Command Decider (Using Base Class with Helpers)

```csharp
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
            SessionReviewCommand.CompleteSession => Event(
                new SessionReviewEvent.SessionCompleted(DateTime.UtcNow)),
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
using Microsoft.Extensions.DependencyInjection;

// Configure services (typically in your startup/program.cs)
services.AddSingleton<IStateFolder<SessionReviewState>, SessionReviewStateFolder>();
services.AddSingleton<ICommandDecider<SessionReviewState, SessionReviewCommand>, SessionReviewCommandDecider>();
services.AddTransient<IAggregateRepository<SessionReviewState>, AggregateRepository<SessionReviewState>>();
services.AddTransient<AggregateCommandExecutor<SessionReviewState, SessionReviewCommand>>();

// Execute commands
using var scope = serviceProvider.CreateScope();

var executor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<SessionReviewState, SessionReviewCommand>>();
var streamId = new StreamIdentifier("SessionReview", "session-1");

// Execute a command (with automatic snapshots if configured on state type and snapshot store is registered)
var command = new SessionReviewCommand.StartSession("session-1", "user-123");
var (newState, newVersion, events) = await executor.ExecuteAsync(
    streamId,
    command);

Console.WriteLine($"New state: {newState.Status}, Version: {newVersion}");
Console.WriteLine($"Events appended: {events.Count}");
```

### 7. Automatic Snapshots

Configure snapshots declaratively on the state type with the `[Aggregate]` attribute:

```csharp
// SnapshotInterval is configured on the STATE TYPE, not the folder
[Aggregate("SessionReview", SnapshotInterval = 50)]
public sealed record SessionReviewState
{
    // ... state properties ...
}

public sealed class SessionReviewStateFolder : StateFolder<SessionReviewState>
{
    public SessionReviewStateFolder(ITypeMetadataRegistry registry) : base(registry) { }
    // ... event handlers ...
}

// In your DI configuration
services.AddSingleton<ISnapshotStore>(sp => /* your snapshot store */);
services.AddSingleton<IStateFolder<SessionReviewState>, SessionReviewStateFolder>();
services.AddTransient<IAggregateRepository<SessionReviewState>, AggregateRepository<SessionReviewState>>();
services.AddTransient<AggregateCommandExecutor<SessionReviewState, SessionReviewCommand>>();

// In your code
using var scope = serviceProvider.CreateScope();
var executor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<SessionReviewState, SessionReviewCommand>>();
var streamId = new StreamIdentifier("SessionReview", "session-1");

var (state, version, events) = await executor.ExecuteAsync(
    streamId,
    command); // Snapshot saved automatically at versions 50, 100, 150, etc.
```

**Snapshot Behavior:**
- `SnapshotInterval = 0` (default) - No automatic snapshots
- `SnapshotInterval > 0` - Snapshots saved every N events
- Snapshots only saved when `ISnapshotStore` is registered in DI
- Idempotent commands (no events) don't trigger snapshots
- Exposed via `StateFolder<TState>.SnapshotInterval` property

## Design Principles

1. **Separation of Concerns**
   - `StateFolder<TState>`: Pure state transitions (events → state)
   - `CommandDecider<TState, TCommand>`: Business logic split into:
     - `ValidateCommand`: Validate against current state
     - `ExecuteCommand`: Produce events (validation already done)
   - `AggregateRepository<TState>`: Persistence lifecycle (load, append, snapshot)
   - `AggregateCommandExecutor<TState, TCommand>`: Command workflow orchestration

2. **Persist First, Fold Second**
   - Events are the source of truth and are persisted **first**
   - State is derived from events and is computed **only when needed** (for snapshots or queries)
   - This architectural principle ensures data safety: events are safely stored before any state derivation

3. **Simplicity**
   - Empty list = idempotent operation (use `NoEvents()` helper)
   - Single event = use `Event(@event)` helper
   - Multiple events = use `Events(event1, event2)` helper
   - Exception = business rule violation
   - Base classes handle common patterns

4. **Validation**
   - `[Aggregate]` attribute required on state types (not on folder or decider implementations)
   - Commands validated against aggregate (via `[Command]` attribute)
   - Events validated against aggregate (via `[Event]` attribute)
   - Event coverage validated at construction: all events must have `When()` handlers
   - Clear error messages guide developers

5. **Type Safety**
   - `StreamEvent` provides full event context in AggregateRepository (pointer, metadata)
   - `IStateFolder.Apply()` works with unwrapped event objects
   - `[Event]` and `[Command]` attributes for proper classification
   - Aggregate boundaries enforced at runtime

6. **DDD Repository Pattern**
   - `IAggregateRepository<TState>` follows Domain-Driven Design repository pattern
   - Encapsulates all persistence concerns (load, append, snapshot)
   - Clean separation between domain logic (decider) and infrastructure (repository)

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
- `CreateStreamId(string identifier)` - **Protected** helper to create StreamIdentifier with aggregate name (for use within decider subclasses; consumers should use `new StreamIdentifier(aggregateName, id)` directly)
- `AggregateName` property - **Protected** property that gets the aggregate name from `[Aggregate]` attribute

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

`AggregateRepository.LoadStateAsync` validates:
- ✅ Stream identifier consistency
- ✅ Sequential version numbering (1, 2, 3...)
- ✅ No gaps in versions
- ✅ No duplicate versions
- ✅ No null events

## Integration with Rickten.EventStore

`AggregateCommandExecutor.ExecuteAsync` workflow:
1. Load current state from repository (with validation)
2. Execute command via decider to produce events
3. Append events to event store with optimistic concurrency (persist first)
4. Fold new events into state and save snapshot if at interval (derive state second)
5. Return updated state, version, and appended events

`AggregateRepository` provides:
- `LoadStateAsync` - Load state from events + snapshots with validation
- `AppendEventsAsync` - Persist events to event store
- `SaveSnapshotIfNeededAsync` - Apply events and save snapshot at configured interval

## Examples

See `Rickten.Aggregator.Examples` namespace for a complete SessionReview implementation:
- `SessionReviewState` + `SessionReviewStateFolder : StateFolder<>`
- `SessionReviewCommand` + `SessionReviewCommandDecider : CommandDecider<>`
- `SessionReviewEvent` with `[Event]` attributes

Run tests in `Rickten.Aggregator.Tests` to see usage patterns.

## License

MIT

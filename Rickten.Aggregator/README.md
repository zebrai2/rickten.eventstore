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
    Task<(TState State, StreamPointer Pointer)> LoadStateAsync(
        StreamIdentifier streamIdentifier, 
        CancellationToken cancellationToken = default);

    // Persist events to the event store
    Task<IReadOnlyList<StreamEvent>> AppendEventsAsync(
        StreamPointer expectedPointer, 
        IReadOnlyList<AppendEvent> events, 
        CancellationToken cancellationToken = default);

    // Validate that events can be folded without errors (pre-append safety check)
    TState ValidateFold(
        TState currentState, 
        IReadOnlyList<object> events);

    // Save snapshot if at configured interval
    Task SaveSnapshotIfNeededAsync(
        TState newState,
        long previousVersion, 
        StreamPointer finalPointer, 
        CancellationToken cancellationToken = default);
}
```

**Architecture Principle: Validate Before Persist**
- Events are the source of truth and must be **valid before persistence** (for stateful commands)
- **ValidateFold** runs for every stateful command that produces events (mandatory safety check)
- ValidateFold ensures events can be replayed without errors before append
- Events are persisted only after validation succeeds
- The validated state from ValidateFold is used for the command result and optional snapshots
- This ensures data safety: invalid events are rejected before they corrupt the stream
- **Exception**: Stateless commands skip ValidateFold for performance - validation is deferred to subsequent stateful commands

### AggregateCommandExecutor<TState, TCommand>

Orchestrates the command execution workflow:

```csharp
public class AggregateCommandExecutor<TState, TCommand>
{
    // Execute command: load state → validate version → decide → validate fold → persist → snapshot
    Task<(TState State, StreamPointer Pointer, IReadOnlyList<StreamEvent> Events)> ExecuteAsync(
        StreamIdentifier streamIdentifier,
        TCommand command,
        IReadOnlyList<AppendMetadata> metadata,
        CancellationToken cancellationToken = default);
}
```

**Workflow:**
1. Check if command is stateless (via `[Command(Stateless = true)]`)
2. **Stateful path (default)**:
   - Load current state from repository (events + snapshots)
   - Validate expected version (if required by command)
   - Execute command via decider to produce events
   - **Validate fold** (ensure events can be replayed without errors - returns new state)
   - Persist events to event store (only after validation)
   - Save snapshot if at interval (using the validated state)
3. **Stateless path** (when `Stateless = true`):
   - Get current stream version (efficient database query)
   - Execute command via decider with initial state
   - Persist events to event store (no fold validation)
   - Snapshot updated if needed

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
- `AggregateCommandExecutor.ExecuteAsync` automatically saves snapshots when append crosses interval boundary (when a `snapshotStore` is registered)
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
- `Stateless` (optional, default `false`) - Skip state loading and fold validation for performance

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

var (state, pointer, events) = await executor.ExecuteAsync(
    streamId,
    new CancelOrder("order-1"),
    []); // No metadata needed
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

## Stateless Commands

Commands can be marked as stateless when they don't require loading or validating the current aggregate state before execution. This optimization is useful for append-only operations, external integrations, or high-throughput scenarios.

### When to Use Stateless Commands

**Good candidates:**
- Append-only operations that don't need to validate current state
- External system integrations where events are sourced from outside systems
- High-throughput logging or audit trails
- Commands that produce events based solely on the command data

**Not suitable for:**
- Commands that need business rules validated against current state
- Commands that check for duplicate operations
- Commands that modify existing state based on current values

### Defining Stateless Commands

Set `Stateless = true` on the `[Command]` attribute:

```csharp
[Command("AuditLog", Stateless = true)]
public sealed record RecordAuditEntry(string UserId, string Action, DateTime Timestamp);

[Command("ExternalEvents", Stateless = true)]
public sealed record ImportExternalEvent(string SourceSystem, string EventType, string Payload);
```

### Stateless Execution Behavior

When `Stateless = true`:

| Feature | Stateful (default) | Stateless |
|---------|-------------------|-----------|
| **State Loading** | ✅ Loads all events + snapshots | ❌ Skipped - uses initial state |
| **Expected Version** | ✅ Validates if `ExpectedVersionKey` set | ❌ Ignored even if set |
| **Fold Validation** | ✅ Validates events can be folded before append | ❌ Skipped - trusts decider output |
| **Optimistic Concurrency** | ✅ Version checked on append | ✅ Version checked on append |
| **Decider Receives** | Current aggregate state | Initial state |

**Important Trade-offs:**

1. **No Pre-Append Validation**: Events are appended without running `ValidateFold`. The decider must produce valid events because they won't be validated before persistence.

2. **Deferred Validation**: Events are validated when they're loaded by subsequent stateful commands. Invalid events will cause those commands to fail during state loading.

3. **State Parameter**: The command decider receives `InitialState()` instead of the current aggregate state. Design your decider to work with this.

4. **Concurrency Still Protected**: The event store still enforces optimistic concurrency on append - concurrent appends will conflict even for stateless commands.

### Example: Stateless Audit Log

```csharp
// State doesn't matter for append-only audit logs
[Aggregate("AuditLog")]
public sealed record AuditLogState
{
    public int EntryCount { get; init; }
}

public sealed class AuditLogStateFolder : StateFolder<AuditLogState>
{
    public override AuditLogState InitialState() => new AuditLogState();

    protected AuditLogState When(AuditEntryRecorded e, AuditLogState state) =>
        state with { EntryCount = state.EntryCount + 1 };
}

// Stateless command - doesn't need to load state
[Command("AuditLog", Stateless = true)]
public sealed record RecordAuditEntry(string UserId, string Action, DateTime Timestamp);

public sealed class AuditLogCommandDecider : CommandDecider<AuditLogState, AuditLogCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(AuditLogState state, AuditLogCommand command)
    {
        return command switch
        {
            RecordAuditEntry r => Event(new AuditEntryRecorded(r.UserId, r.Action, r.Timestamp)),
            _ => throw new InvalidOperationException($"Unknown command: {command}")
        };
    }
}

// Execute without loading state
var executor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<AuditLogState, AuditLogCommand>>();
var streamId = new StreamIdentifier("AuditLog", "user-123");

var (state, pointer, events) = await executor.ExecuteAsync(
    streamId,
    new RecordAuditEntry("user-123", "LoginAttempt", DateTime.UtcNow),
    []);
// State will be InitialState(), pointer will have correct version for optimistic concurrency
```

### Validation Architecture

The stateless command model maintains data safety through **deferred validation**:

```
Stateless Command → Decider (receives initial state) → Events Appended (no ValidateFold)
                                                              ↓
                                                     Persisted to Event Store
                                                              ↓
Next Stateful Command → LoadStateAsync → Fold Events → ValidateFold (validates ALL events)
                                                              ↓
                                                  Invalid Event = Command Fails
```

**Key Insight**: You don't skip validation - you defer it to the next stateful operation. This is acceptable when:
- The stateless command is trusted to produce valid events
- Invalid events failing future commands is an acceptable trade-off
- The performance gain from skipping state load justifies the risk

### Performance Impact

For streams with N events:

**Stateful Command:**
- Load N events from database
- Deserialize N events  
- Fold N events into state
- Execute decider
- Validate fold with new events
- Append M new events

**Stateless Command:**
- Query current stream version (single DB query)
- Execute decider with initial state
- Append M new events (no fold validation)

The performance difference scales with stream size - larger streams benefit more from stateless execution.

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
var (newState, newPointer, events) = await executor.ExecuteAsync(
    streamId,
    command,
    []); // Empty metadata array

Console.WriteLine($"New state: {newState.Status}, Pointer: {newPointer}");
Console.WriteLine($"Events appended: {events.Count}");
```

### 7. Automatic Snapshots (Optional)

Snapshots are **optional**. If you don't need snapshots, simply omit `ISnapshotStore` from your DI registration—the repository will work fine without it.

To enable snapshots, configure them declaratively on the state type with the `[Aggregate]` attribute:

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

// In your DI configuration (snapshots enabled)
services.AddSingleton<ISnapshotStore>(sp => /* your snapshot store */);
services.AddSingleton<IStateFolder<SessionReviewState>, SessionReviewStateFolder>();
services.AddSingleton<ICommandDecider<SessionReviewState, SessionReviewCommand>, SessionReviewCommandDecider>();
services.AddTransient<IAggregateRepository<SessionReviewState>, AggregateRepository<SessionReviewState>>();
services.AddTransient<AggregateCommandExecutor<SessionReviewState, SessionReviewCommand>>();

// OR without snapshots (omit ISnapshotStore)
services.AddSingleton<IStateFolder<SessionReviewState>, SessionReviewStateFolder>();
services.AddSingleton<ICommandDecider<SessionReviewState, SessionReviewCommand>, SessionReviewCommandDecider>();
services.AddTransient<IAggregateRepository<SessionReviewState>, AggregateRepository<SessionReviewState>>();
services.AddTransient<AggregateCommandExecutor<SessionReviewState, SessionReviewCommand>>();

// In your code
using var scope = serviceProvider.CreateScope();
var executor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<SessionReviewState, SessionReviewCommand>>();
var streamId = new StreamIdentifier("SessionReview", "session-1");

var (state, pointer, events) = await executor.ExecuteAsync(
    streamId,
    command,
    []); // Snapshot saved automatically when append crosses/lands on interval boundary
```

**Snapshot Behavior:**
- `SnapshotInterval = 0` (default) - No automatic snapshots
- `SnapshotInterval > 0` - Snapshot saved when append crosses or lands on interval boundary
- Snapshot is saved at the **final appended pointer** (not necessarily exact interval version)
- Example: interval=50, append from v49→v51 saves snapshot at v51 (crossed boundary)
- Snapshots only saved when `ISnapshotStore` is registered in DI
- Idempotent commands (no events) don't trigger snapshots
- Exposed via `StateFolder<TState>.SnapshotInterval` property

## Design Principles

1. **Separation of Concerns**
   - `StateFolder<TState>`: Pure state transitions (events → state)
   - `CommandDecider<TState, TCommand>`: Business logic split into:
     - `ValidateCommand`: Validate against current state
     - `ExecuteCommand`: Produce events (validation already done)
   - `AggregateRepository<TState>`: Persistence lifecycle (load, append, validate, snapshot)
   - `AggregateCommandExecutor<TState, TCommand>`: Command workflow orchestration

2. **Validate Before Persist**
   - Events are the source of truth and must be **valid before persistence**
   - **ValidateFold** runs for every command that produces events (mandatory safety check)
   - ValidateFold ensures events can be replayed without errors before append
   - Events are persisted **only after validation succeeds**
   - The validated state is used for command results and optional snapshots
   - This ensures data safety: invalid events are rejected before they corrupt the stream

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
   - Encapsulates all persistence concerns (load, append, validate, snapshot)
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
2. Validate expected version if required by command
3. Execute command via decider to produce events
4. Validate fold (ensure events can be replayed without errors - returns new state)
5. Append events to event store with optimistic concurrency (only after validation)
6. Save snapshot if at interval using the validated state
7. Return new state, pointer, and appended events

`AggregateRepository` provides:
- `LoadStateAsync` - Load state from events + snapshots with validation
- `ValidateFold` - Validate events can be folded without errors (pre-append safety check)
- `AppendEventsAsync` - Persist events to event store
- `SaveSnapshotIfNeededAsync` - Save snapshot at configured interval using already-validated state

## Examples

See `Rickten.Aggregator.Examples` namespace for a complete SessionReview implementation:
- `SessionReviewState` + `SessionReviewStateFolder : StateFolder<>`
- `SessionReviewCommand` + `SessionReviewCommandDecider : CommandDecider<>`
- `SessionReviewEvent` with `[Event]` attributes

Run tests in `Rickten.Aggregator.Tests` to see usage patterns.

## License

MIT

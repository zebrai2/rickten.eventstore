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
- **Requires `[Aggregate]` attribute** for validation
- Validates event coverage by default (opt-out with `ValidateEventCoverage = false`)
- Handles null event checks
- Provides clean `ApplyEvent(state, event)` method to override
- Offers `EnsureValid(condition, message)` helper for state validation

**CommandDecider<TState, TCommand>** - Abstract base class that:
- **Requires `[Aggregate]` attribute** for validation
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

    // Execute command, append events, return new state
    Task<(TState State, long Version, IReadOnlyList<StreamEvent> Events)> ExecuteAsync<TState, TCommand>(...);
}
```

## Attributes

### [Aggregate]

Required on all `StateFolder` and `CommandDecider` implementations:

```csharp
[Aggregate("SessionReview")]
public sealed class SessionReviewStateFolder : StateFolder<SessionReviewState>
{
    // ...
}

[Aggregate("SessionReview")]
public sealed class SessionReviewCommandDecider : CommandDecider<SessionReviewState, SessionReviewCommand>
{
    // ...
}
```

Properties:
- `Name` (required) - The aggregate name
- `ValidateEventCoverage` (optional, default `true`) - Enable/disable event coverage validation

### [Command]

Optional but recommended on command types for validation:

```csharp
[Command("SessionReview", Name = "Start Session", Description = "Starts a new review session")]
public sealed record StartSession(string SessionId, string UserId) : SessionReviewCommand;
```

Properties:
- `Aggregate` (required) - Which aggregate the command belongs to
- `Name` (optional) - Human-readable command name
- `Description` (optional) - What the command does

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
public sealed record SessionReviewState
{
    public string? SessionId { get; init; }
    public string? UserId { get; init; }
    public SessionStatus Status { get; init; }
    public List<Interaction> Interactions { get; init; } = [];
}
```

### 4. Implement State Folder (Using Base Class)

```csharp
[Aggregate("SessionReview")]
public sealed class SessionReviewStateFolder : StateFolder<SessionReviewState>
{
    public override SessionReviewState InitialState() => new() 
    { 
        Status = SessionStatus.NotStarted 
    };

    protected override SessionReviewState ApplyEvent(SessionReviewState state, object @event)
    {
        return @event switch
        {
            SessionReviewEvent.SessionStarted started => state with
            {
                SessionId = started.SessionId,
                UserId = started.UserId,
                Status = SessionStatus.Active
            },
            SessionReviewEvent.InteractionRecorded interaction => state with
            {
                Interactions = [.. state.Interactions, new Interaction(
                    interaction.InteractionId,
                    interaction.Type,
                    interaction.Content,
                    interaction.RecordedAt)]
            },
            _ => state
        };
    }
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
var eventStore = ...; // IEventStore
var folder = new SessionReviewStateFolder();
var decider = new SessionReviewCommandDecider();

// Use the helper to create stream identifier
var streamId = decider.CreateStreamId("session-1");

// Execute a command
var command = new SessionReviewCommand.StartSession("session-1", "user-123");
var (newState, newVersion, events) = await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    command);

Console.WriteLine($"New state: {newState.Status}, Version: {newVersion}");
Console.WriteLine($"Events appended: {events.Count}");
```

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

3. **Strict Validation by Default**
   - `[Aggregate]` attribute required on implementations
   - Commands validated against aggregate (via `[Command]` attribute)
   - Events validated against aggregate (via `[Event]` attribute)
   - Event coverage validation enabled by default (opt-out available)
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
- `IgnoredEvents` - Override to list event types that should be ignored in coverage validation

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

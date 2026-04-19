# Rickten.Aggregator Architecture

## Overview

Rickten.Aggregator implements event-sourced aggregates following Domain-Driven Design (DDD) principles with a clean separation between domain logic and infrastructure concerns.

## Core Architectural Principles

### 1. Validate Before Persist

**Events are the source of truth and must be valid before persistence.** State is derived from events.

```
┌─────────────────────────────────────────────────────────┐
│                 Command Execution Flow                   │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  1. Load State (from events + snapshots)                │
│     │                                                    │
│     v                                                    │
│  2. Validate Expected Version (if required)             │
│     │                                                    │
│     v                                                    │
│  3. Execute Command → Decide Events                     │
│     │                                                    │
│     v                                                    │
│  4. ✅ VALIDATE FOLD (pre-append safety check) ✅       │
│     │  (ensures events can be replayed without errors)  │
│     │  (returns validated new state)                    │
│     │                                                    │
│     v                                                    │
│  5. ✅ PERSIST EVENTS ✅                                │
│     │  (events safely stored in event store)            │
│     │                                                    │
│     v                                                    │
│  6. Save Snapshot (if at interval)                      │
│     │  (using already-validated state from step 4)      │
│     │                                                    │
│     v                                                    │
│  7. Return (state, pointer, events)                     │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

**Why This Matters:**
- Events are immutable history - validated before persistence
- **ValidateFold** ensures events can be replayed without errors before committing
- If validation fails, nothing is persisted (no corrupt event streams)
- State reconstruction is guaranteed to succeed (validated during append)
- Snapshots use the already-validated state from ValidateFold

### 2. Separation of Concerns

The library separates responsibilities into distinct layers:

```
┌────────────────────────────────────────────────────┐
│              Domain Layer                          │
│  (Pure business logic - no infrastructure)         │
├────────────────────────────────────────────────────┤
│                                                     │
│  IStateFolder<TState>                              │
│  ├─ InitialState() → TState                        │
│  └─ Apply(state, event) → TState                   │
│     Pure function: events → state                  │
│                                                     │
│  ICommandDecider<TState, TCommand>                 │
│  └─ Execute(state, command) → events[]             │
│     Pure function: (state, command) → events       │
│                                                     │
└────────────────────────────────────────────────────┘
                         │
                         v
┌────────────────────────────────────────────────────┐
│         Infrastructure Layer                       │
│  (Persistence, orchestration, snapshots)           │
├────────────────────────────────────────────────────┤
│                                                     │
│  IAggregateRepository<TState>                      │
│  ├─ LoadStateAsync(streamId) → (state, pointer)   │
│  │   Loads from events + snapshots                 │
│  ├─ ValidateFold(state, events) → newState        │
│  │   Validates events before append                │
│  ├─ AppendEventsAsync(pointer, events) → events   │
│  │   Persists events to event store                │
│  └─ SaveSnapshotIfNeededAsync(state, prevVer, ptr)│
│      Saves snapshot using validated state          │
│                                                     │
│  AggregateCommandExecutor<TState, TCommand>        │
│  └─ ExecuteAsync(streamId, command, metadata)     │
│      → (state, pointer, events)                    │
│      Orchestrates: load → decide → validate → persist → snapshot │
│                                                     │
└────────────────────────────────────────────────────┘
```

### 3. DDD Repository Pattern

`IAggregateRepository<TState>` follows the Repository pattern from Domain-Driven Design:

**Responsibilities:**
- ✅ Load aggregates from storage (events + snapshots)
- ✅ Persist aggregate changes (events)
- ✅ Manage snapshots for optimization
- ✅ Validate stream integrity (version continuity)

**Benefits:**
- Domain logic (folders, deciders) has no dependency on infrastructure
- Infrastructure concerns (event store, snapshot store) are encapsulated
- Easy to test domain logic in isolation
- Clear contract for aggregate persistence

### 4. Command Executor Pattern

`AggregateCommandExecutor<TState, TCommand>` orchestrates the command execution workflow:

**Workflow:**
```csharp
public async Task<(TState, StreamPointer, IReadOnlyList<StreamEvent>)> ExecuteAsync(
    StreamIdentifier streamIdentifier,
    TCommand command,
    IReadOnlyList<AppendMetadata> metadata,
    CancellationToken cancellationToken = default)
{
    // Step 1: Load current state
    var (state, currentPointer) = await repository.LoadStateAsync(streamIdentifier, ct);

    // Step 2: Validate expected version (if command declares ExpectedVersionKey)
    if (expectedVersion.HasValue && currentPointer.Version != expectedVersion.Value)
        throw new StreamVersionConflictException(...);

    // Step 3: Execute command → produce events
    var events = decider.Execute(state, command);
    if (events.Count == 0) return (state, currentPointer, []); // idempotent

    // Step 4: ✅ VALIDATE FOLD (pre-append safety check) ✅
    var newState = repository.ValidateFold(state, events);

    // Step 5: Filter metadata and wrap events for append
    var filteredMetadata = metadata.Filter(expectedVersionKey);
    var appendEvents = events.ToAppendEvent(filteredMetadata);

    // Step 6: ✅ PERSIST EVENTS ✅
    var appendedEvents = await repository.AppendEventsAsync(currentPointer, appendEvents, ct);

    // Step 7: Save snapshot if at interval
    var finalPointer = appendedEvents[^1].StreamPointer;
    await repository.SaveSnapshotIfNeededAsync(newState, currentPointer.Version, finalPointer, ct);

    return (newState, finalPointer, appendedEvents);
}
```

**Expected Version Support:**
- Commands can declare `ExpectedVersionKey` in `[Command]` attribute
- Executor validates expected version **before** running decider
- Prevents stale-read issues in CQRS systems
- Expected version metadata is **not persisted** with events (request context only)

## Component Responsibilities

### StateFolder<TState>

**What:** Pure state fold function (events → state)

**Responsibilities:**
- Define initial state
- Define event handlers (`When` methods)
- Validate event coverage (all events have handlers)
- Provide state validation helpers

**Does NOT:**
- Access event store
- Access snapshot store
- Know about commands
- Know about persistence

**Example:**
```csharp
public class OrderStateFolder : StateFolder<OrderState>
{
    public OrderStateFolder(ITypeMetadataRegistry registry) : base(registry) { }

    public override OrderState InitialState() => new();

    protected OrderState When(OrderPlaced e, OrderState state) =>
        state with { OrderId = e.OrderId, Status = OrderStatus.Pending };

    protected OrderState When(OrderApproved e, OrderState state) =>
        state with { Status = OrderStatus.Approved, ApprovedAt = e.Timestamp };
}
```

### CommandDecider<TState, TCommand>

**What:** Pure decision function ((state, command) → events)

**Responsibilities:**
- Validate command against current state
- Produce events based on command + state
- Enforce business rules
- Handle idempotent commands

**Does NOT:**
- Access event store
- Fold events into state
- Know about persistence
- Know about snapshots

**Example:**
```csharp
public class OrderCommandDecider : CommandDecider<OrderState, OrderCommand>
{
    protected override void ValidateCommand(OrderState state, OrderCommand command)
    {
        if (command is ApproveOrder)
            RequireEqual(state.Status, OrderStatus.Pending, "Order must be pending");
    }

    protected override IReadOnlyList<object> ExecuteCommand(OrderState state, OrderCommand command)
    {
        return command switch
        {
            PlaceOrder place => Event(new OrderPlaced(place.OrderId, DateTime.UtcNow)),
            ApproveOrder => Event(new OrderApproved(DateTime.UtcNow)),
            _ => NoEvents() // idempotent
        };
    }
}
```

### AggregateRepository<TState>

**What:** Persistence layer for aggregates following DDD Repository pattern

**Responsibilities:**
- Load state from events + snapshots
- Validate stream integrity (version continuity, no gaps, no nulls)
- Validate fold before persist (pre-append safety check)
- Persist events to event store
- Save snapshots at configured intervals using validated state

**Does NOT:**
- Execute commands
- Make business decisions
- Know about command metadata filtering

**Key Methods:**
```csharp
// Load state from storage (events + optional snapshot)
Task<(TState State, StreamPointer Pointer)> LoadStateAsync(
    StreamIdentifier streamIdentifier,
    CancellationToken cancellationToken);

// Validate fold before persist (pre-append safety check)
TState ValidateFold(
    TState currentState,
    IReadOnlyList<object> events);

// Persist events (pure I/O)
Task<IReadOnlyList<StreamEvent>> AppendEventsAsync(
    StreamPointer expectedPointer,
    IReadOnlyList<AppendEvent> events,
    CancellationToken cancellationToken);

// Save snapshot if at interval (using already-validated state)
Task SaveSnapshotIfNeededAsync(
    TState newState,
    long previousVersion,
    StreamPointer finalPointer,
    CancellationToken cancellationToken);
```

### AggregateCommandExecutor<TState, TCommand>

**What:** Command execution workflow orchestrator

**Responsibilities:**
- Load current state via repository
- Handle expected version validation (if command declares `ExpectedVersionKey`)
- Execute command via decider
- Validate fold before persist (pre-append safety check)
- Filter metadata (remove expected version key before persistence)
- Persist events via repository
- Save snapshot via repository using validated state
- Return new state + pointer + events

**Does NOT:**
- Validate fold (delegates to repository)
- Access event/snapshot stores directly (uses repository)
- Make business decisions (delegates to decider)

**Usage:**
```csharp
// Configure DI
services.AddSingleton<IStateFolder<OrderState>, OrderStateFolder>();
services.AddSingleton<ICommandDecider<OrderState, OrderCommand>, OrderCommandDecider>();
services.AddTransient<IAggregateRepository<OrderState>, AggregateRepository<OrderState>>();
services.AddTransient<AggregateCommandExecutor<OrderState, OrderCommand>>();

// Execute command
var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<OrderState, OrderCommand>>();
var streamId = new StreamIdentifier("Order", "order-123");

var (state, pointer, events) = await executor.ExecuteAsync(
    streamId,
    new ApproveOrder("order-123"),
    []); // metadata
```

## Data Flow

### Command Execution Flow

```
User/API
   │
   │ command + metadata
   │
   v
┌──────────────────────────────────────────────┐
│     AggregateCommandExecutor                 │
│  ┌────────────────────────────────────────┐  │
│  │ 1. Load State                          │  │
│  │    repository.LoadStateAsync()         │  │
│  │    ↓                                   │  │
│  │    ├─ Load snapshot (if exists)        │  │
│  │    ├─ Load events after snapshot       │  │
│  │    ├─ Validate version continuity      │  │
│  │    └─ Fold events → state              │  │
│  └────────────────────────────────────────┘  │
│                  │                            │
│                  v                            │
│  ┌────────────────────────────────────────┐  │
│  │ 2. Validate Expected Version (if set) │  │
│  │    Extract from metadata               │  │
│  │    Compare with current version        │  │
│  │    Throw if mismatch                   │  │
│  └────────────────────────────────────────┘  │
│                  │                            │
│                  v                            │
│  ┌────────────────────────────────────────┐  │
│  │ 3. Execute Command                     │  │
│  │    decider.Execute(state, command)     │  │
│  │    ↓                                   │  │
│  │    ├─ ValidateCommand()                │  │
│  │    └─ ExecuteCommand() → events[]      │  │
│  └────────────────────────────────────────┘  │
│                  │                            │
│                  v                            │
│  ┌────────────────────────────────────────┐  │
│  │ 4. ✅ VALIDATE FOLD ✅                │  │
│  │    repository.ValidateFold()           │  │
│  │    ↓                                   │  │
│  │    └─ folder.Apply() for each event    │  │
│  │       (pre-append safety check)        │  │
│  └────────────────────────────────────────┘  │
│                  │                            │
│                  v                            │
│  ┌────────────────────────────────────────┐  │
│  │ 5. ✅ PERSIST EVENTS ✅               │  │
│  │    repository.AppendEventsAsync()      │  │
│  │    ↓                                   │  │
│  │    └─ eventStore.AppendAsync()         │  │
│  │       (with optimistic concurrency)    │  │
│  └────────────────────────────────────────┘  │
│                  │                            │
│                  v                            │
│  ┌────────────────────────────────────────┐  │
│  │ 6. Snapshot (if at interval)           │  │
│  │    repository.SaveSnapshotIfNeeded()   │  │
│  │    ↓                                   │  │
│  │    ├─ Check if at snapshot interval    │  │
│  │    └─ Save snapshot (using validated   │  │
│  │       state from step 4)               │  │
│  └────────────────────────────────────────┘  │
│                  │                            │
│                  v                            │
│         (state, pointer, events)              │
└──────────────────────────────────────────────┘
   │
   v
Return to caller
```

### Load State Flow

```
┌──────────────────────────────────────┐
│  AggregateRepository.LoadStateAsync  │
│  ┌────────────────────────────────┐  │
│  │ 1. Load Snapshot (if exists)   │  │
│  │    snapshotStore?.LoadAsync()  │  │
│  │    ↓                           │  │
│  │    state = snapshot ?? initial │  │
│  │    version = snapshot.Version  │  │
│  └────────────────────────────────┘  │
│              │                        │
│              v                        │
│  ┌────────────────────────────────┐  │
│  │ 2. Load Events After Snapshot  │  │
│  │    eventStore.ReadAsync()      │  │
│  │    (from version+1 onwards)    │  │
│  └────────────────────────────────┘  │
│              │                        │
│              v                        │
│  ┌────────────────────────────────┐  │
│  │ 3. Validate Stream Integrity   │  │
│  │    ├─ Check stream IDs match   │  │
│  │    ├─ Check versions 1,2,3...  │  │
│  │    ├─ Check no gaps            │  │
│  │    ├─ Check no duplicates      │  │
│  │    └─ Check no null events     │  │
│  └────────────────────────────────┘  │
│              │                        │
│              v                        │
│  ┌────────────────────────────────┐  │
│  │ 4. Fold Events → State         │  │
│  │    foreach event:              │  │
│  │      state = folder.Apply()    │  │
│  └────────────────────────────────┘  │
│              │                        │
│              v                        │
│      (state, pointer)                 │
└──────────────────────────────────────┘
```

## Snapshot Strategy

**Declarative Configuration:**
```csharp
[Aggregate("Order", SnapshotInterval = 50)]
public record OrderState { ... }
```

**Automatic Behavior:**
- Executor calls `repository.SaveSnapshotIfNeededAsync()` after persisting events
- Repository checks if append crossed or landed on an interval boundary
- Snapshot saved at the **final appended pointer** when boundary is crossed
- Example: interval=50, append from v49→v51 saves snapshot at v51
- Load operations start from latest snapshot
- Snapshots are **optional optimization** - events are source of truth

**Why Validate Before Persist:**
- Events must be validated before persistence to prevent corrupt streams
- ValidateFold ensures events can be replayed without errors
- If validation fails, nothing is persisted (data safety)
- Snapshot uses the already-validated state from ValidateFold
- State reconstruction is guaranteed to succeed (validated during append)

## Error Handling

### Business Rule Violations
```csharp
// In CommandDecider.ValidateCommand
protected override void ValidateCommand(OrderState state, OrderCommand command)
{
    if (command is ApproveOrder)
        RequireEqual(state.Status, OrderStatus.Pending, "Order must be pending");
    // Throws InvalidOperationException if validation fails
}
```

### Concurrency Conflicts
```csharp
// In AggregateRepository.AppendEventsAsync
await _eventStore.AppendAsync(pointer, events, cancellationToken);
// Throws StreamVersionConflictException if version mismatch
```

### Expected Version Mismatch
```csharp
// In AggregateCommandExecutor.ExecuteAsync
if (expectedVersionKey != null)
{
    var expectedVersion = ExtractExpectedVersion(metadata, expectedVersionKey);
    if (currentVersion != expectedVersion)
        throw new StreamVersionConflictException(...);
}
```

### Stream Integrity Violations
```csharp
// In AggregateRepository.LoadStateAsync
// Validates:
- Stream identifier consistency → InvalidOperationException
- Version continuity (1,2,3...) → InvalidOperationException
- No gaps in versions → InvalidOperationException
- No duplicate versions → InvalidOperationException
- No null events → InvalidOperationException
```

## Testing Strategy

### Unit Tests (Domain Layer)

Test folders and deciders in isolation:

```csharp
[Fact]
public void StateFolder_When_OrderPlaced_SetsOrderId()
{
    var folder = new OrderStateFolder(registry);
    var state = folder.InitialState();
    var @event = new OrderPlaced("order-123", DateTime.UtcNow);

    var newState = folder.Apply(state, @event);

    Assert.Equal("order-123", newState.OrderId);
}

[Fact]
public void CommandDecider_ApproveOrder_RequiresPendingStatus()
{
    var decider = new OrderCommandDecider();
    var state = new OrderState { Status = OrderStatus.Approved }; // wrong status
    var command = new ApproveOrder();

    var ex = Assert.Throws<InvalidOperationException>(
        () => decider.Execute(state, command));
    Assert.Contains("must be pending", ex.Message);
}
```

### Integration Tests (Infrastructure Layer)

Test repository and executor with real event store:

```csharp
[Fact]
public async Task AggregateRepository_LoadStateAsync_LoadsFromSnapshot()
{
    // Arrange: Create snapshot + events
    await snapshotStore.SaveAsync(...);
    await eventStore.AppendAsync(...);

    // Act
    var (state, pointer) = await repository.LoadStateAsync(streamId);

    // Assert
    Assert.Equal(expectedVersion, pointer.Version);
    Assert.Equal(expectedState, state);
}

[Fact]
public async Task AggregateCommandExecutor_ExecuteAsync_ValidatesThenPersists()
{
    // Arrange
    var executor = new AggregateCommandExecutor<OrderState, OrderCommand>(...);

    // Act
    var (state, pointer, events) = await executor.ExecuteAsync(streamId, command, []);

    // Assert: Events persisted
    var loadedEvents = await eventStore.ReadAsync(...);
    Assert.Equal(events, loadedEvents);

    // Assert: State derived from events
    var (loadedState, loadedPointer) = await repository.LoadStateAsync(streamId);
    Assert.Equal(state, loadedState);
    Assert.Equal(pointer, loadedPointer);
}
```

## Migration from Previous Architecture

### Before (StateRunner static utilities)

```csharp
// Old approach
var (state, version, events) = await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    command,
    registry,
    snapshotStore,
    metadata);
```

### After (DDD Repository + Executor)

```csharp
// Configure DI (once at startup)
services.AddSingleton<IStateFolder<OrderState>, OrderStateFolder>();
services.AddSingleton<ICommandDecider<OrderState, OrderCommand>, OrderCommandDecider>();
services.AddTransient<IAggregateRepository<OrderState>, AggregateRepository<OrderState>>();
services.AddTransient<AggregateCommandExecutor<OrderState, OrderCommand>>();

// Execute command (in your code)
var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<OrderState, OrderCommand>>();
var (state, pointer, events) = await executor.ExecuteAsync(streamId, command, metadata);
```

### Key Changes

1. **StateRunner removed** → Replaced by `AggregateRepository` + `AggregateCommandExecutor`
2. **Static methods** → Instance methods (DI-friendly)
3. **Many parameters** → Dependencies injected via constructor
4. **Unclear responsibilities** → Clear separation (repository = persistence, executor = orchestration)
5. **Fold-then-persist** → **Validate-before-persist** (validate fold before append for data safety)

### Benefits

- ✅ Better testability (inject mocks for repository)
- ✅ Clearer responsibilities (DDD repository pattern)
- ✅ Safer architecture (validate fold before persist to prevent corrupt streams)
- ✅ More idiomatic .NET (DI instead of static utilities)
- ✅ Extensible (can override repository or executor behavior)

## Summary

Rickten.Aggregator provides a clean, testable architecture for event-sourced aggregates:

- **Domain layer** (StateFolder, CommandDecider) is pure and infrastructure-free
- **Infrastructure layer** (AggregateRepository) handles all persistence concerns including fold validation
- **Orchestration layer** (AggregateCommandExecutor) manages the command workflow
- **Events are validated before persistence**, ensuring stream integrity (safe by design)
- **DDD Repository pattern** provides clean abstraction for aggregate persistence
- **Automatic snapshots** via declarative configuration on state types
- **Expected version support** for CQRS stale-read protection

# Rickten Event Sourcing Triangle

## The Complete Picture

Rickten provides three complementary mechanisms that form a complete event-sourcing architecture:

```
┌─────────────┐
│ Aggregator  │  Commands → Events
└──────┬──────┘
       │
       │ produces
       ↓
┌─────────────┐
│   Events    │
└──────┬──────┘
       │
       ├──────────────┐
       │              │
       ↓              ↓
┌─────────────┐  ┌─────────────┐
│  Projector  │  │   Reactor   │
└─────────────┘  └─────────────┘
Events →         Trigger Event +
Read Models      Projection View →
                 Commands
```

## The Three Mechanisms

### 1. **Aggregator**: Commands → Events

**Purpose**: Execute business logic and produce events

**Flow**:
```
Command → StateFolder → CommandDecider → Events
```

**Key Components**:
- `StateFolder<TState>`: Rebuilds aggregate state from events
- `CommandDecider<TState, TCommand>`: Executes commands, produces events
- `StateRunner`: Orchestrates command execution

**Example**:
```csharp
public class UserCommandDecider : CommandDecider<UserState, RegisterUserCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(UserState state, RegisterUserCommand command)
    {
        if (state.IsRegistered)
            throw new InvalidOperationException("User already registered");

        return Event(new UserRegisteredEvent(command.Email));
    }
}
```

---

### 2. **Projector**: Events → Read Models

**Purpose**: Build queryable read models from events

**Flow**:
```
Events → Projection.Apply() → View (Read Model)
```

**Key Components**:
- `Projection<TView>`: Defines how events transform into read model
- `ProjectionRunner`: Catches up projections from checkpoints
- `IProjectionStore`: Persists projection state

**Example**:
```csharp
[Projection("UserSummary")]
public class UserSummaryProjection : Projection<UserSummaryView>
{
    public override UserSummaryView InitialView() => new();

    protected override UserSummaryView ApplyEvent(UserSummaryView view, StreamEvent streamEvent)
    {
        return streamEvent.Event switch
        {
            UserRegisteredEvent e => view with { TotalUsers = view.TotalUsers + 1 },
            _ => view
        };
    }
}
```

---

### 3. **Reactor**: Trigger Event + Projection View → Commands

**Purpose**: Event-driven command execution across aggregate streams

**Flow**:
```
Trigger Event → Projection View → SelectStreams() → BuildCommand() → Commands
```

**Key Components**:
- `Reaction<TView, TCommand>`: Owns projection, selects streams, builds commands
- `ReactionRunner`: Catches up reactions with dual-checkpoint model
- Uses same `IProjectionStore` in "reaction" namespace

**Example**:
```csharp
[Reaction("MembershipDefinitionChanged", EventTypes = new[] { "MembershipDefinition.Changed.v1" })]
public class MembershipReaction : Reaction<MembershipIndexView, RecalculateCommand>
{
    private readonly MembershipIndexProjection _projection = new();

    public override IProjection<MembershipIndexView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(
        MembershipIndexView view, StreamEvent trigger)
    {
        // Use projection to find affected memberships
        var evt = (MembershipDefinitionChangedEvent)trigger.Event;
        foreach (var membershipId in view.GetMemberships(evt.DefinitionId))
        {
            yield return new StreamIdentifier("Membership", membershipId);
        }
    }

    protected override RecalculateCommand BuildCommand(
        StreamIdentifier stream, MembershipIndexView view, StreamEvent trigger)
    {
        return new RecalculateCommand(stream.Identifier, "Definition changed");
    }
}
```

---

## Conceptual Wording

### Precise Definitions

```
Aggregator: command → events
Projector:  events → read model
Reactor:    trigger event + projection view → commands
```

### Why This Wording Matters

1. **"Command" (singular)** → Aggregator processes one command at a time
2. **"Events" (plural)** → Commands can produce multiple events
3. **"Read model" (singular concept)** → Projection builds one coherent view
4. **"Trigger event + projection view"** → Reactor needs BOTH to decide
5. **"Commands" (plural)** → Reactor can execute commands against multiple streams

### Reactor is NOT a Saga

**Saga/Process Manager characteristics** (NOT Reactor):
- ❌ Maintains long-lived state across multiple steps
- ❌ Coordinates multi-stage workflows
- ❌ Has its own state machine
- ❌ Tracks completion of distributed transactions

**Reactor characteristics**:
- ✅ Stateless (except projection view for selection)
- ✅ One trigger → immediate commands
- ✅ No workflow state beyond checkpoints
- ✅ Deterministic stream selection via projection
- ✅ At-least-once delivery (idempotent commands)

---

## How They Work Together

### Example Scenario: User Registration Triggers Membership Creation

#### 1. User Registration (Aggregator)

```csharp
// Command → Events
RegisterUserCommand("user-123", "user@example.com", "def-premium")
  → UserRegisteredEvent("user-123", "user@example.com", "def-premium")
```

#### 2. Build Read Model (Projector)

```csharp
// Events → Read Model
UserRegisteredEvent
  → MembershipIndexView with mapping: "def-premium" → ["user-123"]
```

#### 3. React to Event (Reactor)

```csharp
// Trigger Event + Projection View → Commands
UserRegisteredEvent @ position 42
  + MembershipIndexView (showing def-premium → user-123)
  → SelectStreams: ["Membership/user-123"]
  → CreateMembershipCommand("user-123", "def-premium")
```

#### 4. Execute Command (Back to Aggregator)

```csharp
// Command → Events
CreateMembershipCommand("user-123", "def-premium")
  → MembershipCreatedEvent("user-123", "def-premium", "Active")
```

---

## Key Design Principles

### 1. **Separation of Concerns**

- **Aggregator**: Business logic, invariants, event production
- **Projector**: Query models, denormalization, read optimization
- **Reactor**: Event-driven automation, cross-aggregate orchestration

### 2. **Explicit Dependencies**

- Reactor **owns** its projection (explicit via `Projection` property)
- Reactor **uses** aggregator pipeline (explicit via `StateRunner.ExecuteAsync`)
- All three use **same event store** (single source of truth)

### 3. **No Hidden State**

- Aggregator: State rebuilt from events
- Projector: State persisted in `IProjectionStore` ("system" namespace)
- Reactor: Two checkpoints in `IProjectionStore` ("reaction" namespace)
  - `{name}:trigger` = last trigger fully processed
  - `{name}:projection` = last event applied to projection

### 4. **Mechanism, Not Framework**

- Static runners (no instances, no DI containers)
- Explicit persistence stores (no hidden repositories)
- Attribute metadata (no convention-based magic)
- Manual execution (no automatic hosting)

---

## When to Use Each

### Use Aggregator When:
- Executing business commands
- Enforcing invariants
- Producing domain events
- Implementing business logic

### Use Projector When:
- Building query models
- Denormalizing data for reads
- Creating reports or summaries
- Optimizing read performance

### Use Reactor When:
- One event should trigger commands on multiple aggregates
- Cross-aggregate automation needed
- Event-driven workflows (single-step)
- Fan-out command execution required

---

## What Reactor Is NOT

### ❌ Not a Saga/Process Manager
- No multi-step workflow state
- No compensation logic
- No long-lived state machine

### ❌ Not a Hosted Service
- No background polling loop
- No automatic execution
- Manual `CatchUpAsync` calls

### ❌ Not an Event Handler
- Not for side effects (email, logging)
- Only for commands through aggregator pipeline
- Commands must be idempotent

---

## Historical Projection Guarantee

### The Problem

When replaying triggers after a failure, the projection might be at a different state than when the event originally occurred:

```
Timeline:
Position 1: DefinitionChanged (trigger #1)
Position 2: UserRegistered (projection update only)
Position 3: DefinitionChanged (trigger #2)

FAILURE occurs at position 3 before checkpoint saved

On restart:
- Trigger checkpoint: Position 1
- Projection checkpoint: Position 3 (ahead!)

Without correction:
- Replay trigger #1 at position 1
- But projection shows state at position 3 (wrong!)
```

### The Solution

```csharp
if (projectionPosition > reactionPosition)
{
    // Projection is ahead - rebuild to reaction position
    (view, _) = await ProjectionRunner.RebuildUntilAsync(
        eventStore,
        reaction.Projection,
        untilGlobalPosition: reactionPosition,
        cancellationToken);
}
```

**Guarantee**: Triggers are always evaluated against projection state **as of the trigger's global position**, ensuring deterministic replay behavior.

---

## Summary

The Rickten triangle provides a complete, coherent event-sourcing architecture:

1. **Commands flow INTO aggregates** (Aggregator)
2. **Events flow OUT to projections** (Projector)  
3. **Events flow THROUGH to new commands** (Reactor)

Each mechanism is:
- ✅ Small and focused
- ✅ Explicitly configured
- ✅ Independently testable
- ✅ Manually orchestrated

Together they enable:
- Event-sourced aggregates with business logic
- Optimized read models for queries
- Event-driven cross-aggregate automation

Without becoming:
- A heavy framework
- Magic convention-based
- Automatically hosted
- Process manager/saga

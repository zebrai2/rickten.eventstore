# Rickten.Projector

Lightweight library for building event-sourced projections (read models) with declarative filtering and checkpoint management.

## Features

- ✅ **Simple Projection Interface** - `IProjection<TView>` for building read models
- ✅ **Declarative Filtering** - `[Projection]` attribute with aggregate and event type filters
- ✅ **Efficient Queries** - Leverages `IEventStore.LoadAllAsync` filtering
- ✅ **Checkpoint Management** - Automatic catch-up from last processed version
- ✅ **Full Event Context** - Access to `StreamEvent` with metadata in projections
- ✅ **Flexible Checkpointing** - Manual rebuild or automatic catch-up modes
- ✅ **Filter Validation** - Runtime validation that received events match configured filters

## Installation

```bash
dotnet add package Rickten.Projector
```

## Quick Start

### 1. Define Your View Model

```csharp
public record ActiveSessionsView
{
    public HashSet<string> ActiveSessions { get; init; } = [];
    public int TotalStarted { get; init; }
    public int TotalCompleted { get; init; }
}
```

### 2. Create a Projection

```csharp
[Projection("ActiveSessions",
    AggregateTypes = ["SessionReview"],
    EventTypes = ["SessionStarted", "SessionCompleted", "SessionCancelled"])]
public class ActiveSessionsProjection : Projection<ActiveSessionsView>
{
    public override ActiveSessionsView InitialView() => new();

    protected override ActiveSessionsView ApplyEvent(
        ActiveSessionsView view,
        StreamEvent streamEvent)
    {
        return streamEvent.Event switch
        {
            SessionReviewEvent.SessionStarted started => view with
            {
                ActiveSessions = view.ActiveSessions.Add(started.SessionId),
                TotalStarted = view.TotalStarted + 1
            },

            SessionReviewEvent.SessionCompleted completed => view with
            {
                ActiveSessions = view.ActiveSessions.Remove(completed.SessionId),
                TotalCompleted = view.TotalCompleted + 1
            },

            SessionReviewEvent.SessionCancelled cancelled => view with
            {
                ActiveSessions = view.ActiveSessions.Remove(cancelled.SessionId)
            },

            _ => view
        };
    }
}
```

### 3. Project Events

**Option A: Manual Rebuild** (from scratch or specific version)

```csharp
var eventStore = ...; // IEventStore
var projection = new ActiveSessionsProjection();

// Rebuild from beginning
var (view, lastVersion) = await ProjectionRunner.RebuildAsync(
    eventStore,
    projection);

Console.WriteLine($"Active sessions: {view.ActiveSessions.Count}");
Console.WriteLine($"Last version: {lastVersion}");
```

**Option B: Catch-Up with Checkpoints** (automatic checkpoint management)

```csharp
var eventStore = ...; // IEventStore
var projectionStore = ...; // IProjectionStore
var projection = new ActiveSessionsProjection();

// Loads last checkpoint, processes new events, saves updated checkpoint
var (view, version) = await ProjectionRunner.CatchUpAsync(
    eventStore,
    projectionStore,
    projection);

Console.WriteLine($"Active sessions: {view.ActiveSessions.Count}");
Console.WriteLine($"Checkpoint version: {version}");
```

## Core Concepts

### IProjection<TView>

The fundamental interface for projections:

```csharp
public interface IProjection<TView>
{
    TView InitialView();
    TView Apply(TView view, StreamEvent streamEvent);
}
```

### Projection<TView> Base Class

Abstract base class with attribute-based filtering:

```csharp
public abstract class Projection<TView> : IProjection<TView>
{
    public string ProjectionName { get; }
    public string[]? AggregateTypeFilter { get; }
    public string[]? EventTypeFilter { get; }

    public abstract TView InitialView();
    protected abstract TView ApplyEvent(TView view, StreamEvent streamEvent);
}
```

### [Projection] Attribute

Optional attribute for metadata and filtering:

```csharp
[Projection("ProjectionName",
    AggregateTypes = ["Aggregate1", "Aggregate2"],
    EventTypes = ["Event1", "Event2"],
    Description = "What this projection does")]
```

**Properties:**
- `Name` (required) - Projection identifier for checkpointing
- `AggregateTypes` (optional) - Filter by aggregate types (uses `IEventStore.LoadAllAsync`)
- `EventTypes` (optional) - Filter by event types (uses `IEventStore.LoadAllAsync`)
- `Description` (optional) - Documentation

**Filter Behavior:**
- Filters are passed to `IEventStore.LoadAllAsync` for efficient querying
- Runtime validation ensures received events match filters
- Throws `InvalidOperationException` if mismatch detected
- `null` filters mean "all events"

### ProjectionRunner

Static utility methods for projection operations:

**`RebuildAsync`** - Rebuild projection from scratch:
```csharp
public static Task<(TView View, long LastGlobalPosition)> RebuildAsync<TView>(
    IEventStore eventStore,
    IProjection<TView> projection,
    long fromGlobalPosition = 0,
    CancellationToken cancellationToken = default);
```

**`CatchUpAsync`** - Automatic checkpoint management:
```csharp
public static Task<(TView View, long GlobalPosition)> CatchUpAsync<TView>(
    IEventStore eventStore,
    IProjectionStore projectionStore,
    IProjection<TView> projection,
    string? projectionName = null,
    CancellationToken cancellationToken = default);
```

## Access to Event Metadata

Projections receive the full `StreamEvent` with metadata:

```csharp
protected override MyView ApplyEvent(MyView view, StreamEvent streamEvent)
{
    // Access event
    var @event = streamEvent.Event;

    // Access stream information
    var streamType = streamEvent.StreamPointer.Stream.StreamType;
    var version = streamEvent.StreamPointer.Version;

    // Access metadata
    var metadata = streamEvent.Metadata;
    var correlationId = metadata.FirstOrDefault(m => m.Key == "CorrelationId")?.Value;

    // Use metadata in projection logic
    return view with { /* ... */ };
}
```

## Design Principles

1. **Projection-Controlled** - Each projection decides how to use metadata and checkpoints
2. **Efficient Filtering** - Leverage store-level filtering via `[Projection]` attribute
3. **Simple Mechanics** - Just rebuild or catch-up, no background services
4. **Flexible Checkpointing** - Manual or automatic, projection decides
5. **Filter Validation** - Runtime checks ensure query/filter consistency

## Error Handling

- **Filter Mismatch**: Throws `InvalidOperationException` if received event doesn't match filters
- **Missing Projection Name**: Throws `ArgumentException` in `CatchUpAsync` if name not provided
- **Event Processing**: Projection code handles event-specific errors

## When to Use Each Mode

**Use `RebuildAsync` when:**
- Building projections for the first time
- Need to rebuild from scratch (data corruption, schema change)
- Testing projections
- Don't need checkpoint management
- Want full control over persistence

**Use `CatchUpAsync` when:**
- Production projections with checkpoint management
- Want automatic "catch up to current" behavior
- Need to resume from last processed version
- Prefer `ProjectionStore` to manage checkpoints

## Examples

### Simple Event Counter

```csharp
public record EventCountView(int Count);

[Projection("EventCount")]
public class EventCountProjection : Projection<EventCountView>
{
    public override EventCountView InitialView() => new(0);

    protected override EventCountView ApplyEvent(
        EventCountView view,
        StreamEvent streamEvent)
    {
        return view with { Count = view.Count + 1 };
    }
}
```

### Aggregate-Specific Projection

```csharp
[Projection("UserStats", AggregateTypes = ["User"])]
public class UserStatsProjection : Projection<UserStatsView>
{
    // Only processes events from "User" aggregate
}
```

### Event-Specific Projection

```csharp
[Projection("CompletedSessions",
    EventTypes = ["SessionCompleted"])]
public class CompletedSessionsProjection : Projection<CompletedView>
{
    // Only processes SessionCompleted events
}
```

### Using Metadata

```csharp
protected override AuditView ApplyEvent(AuditView view, StreamEvent streamEvent)
{
    var timestamp = streamEvent.Metadata
        .FirstOrDefault(m => m.Key == "Timestamp")?.Value as DateTime?;

    var userId = streamEvent.Metadata
        .FirstOrDefault(m => m.Key == "UserId")?.Value as string;

    // Use metadata in projection logic
    return view with { /* ... */ };
}
```

## Relationship to Other Packages

- **Rickten.EventStore** - Provides `IEventStore` and `IProjectionStore` interfaces
- **Rickten.Aggregator** - Write-side aggregate patterns (commands → events → state)
- **Rickten.Projector** - Read-side projection patterns (events → views)

Projections and aggregates are complementary:
- Aggregates enforce business rules and produce events
- Projections build optimized read models from those events

---

For more information, see the [main repository](https://github.com/zebrai2/rickten.eventstore).

# API Reference

Complete API documentation for Rickten.EventStore.

## Table of Contents

- [Core Abstractions](#core-abstractions)
  - [IEventStore](#ieventstore)
  - [ISnapshotStore](#isnapshotstore)
  - [IProjectionStore](#iprojectionstore)
- [Data Types](#data-types)
  - [StreamIdentifier](#streamidentifier)
  - [StreamPointer](#streampointer)
  - [AppendEvent](#appendevent)
  - [StreamEvent](#streamevent)
  - [AppendMetadata](#appendmetadata)
  - [EventMetadata](#eventmetadata)
  - [Snapshot](#snapshot)
  - [Projection](#projection)
- [Attributes](#attributes)
  - [EventAttribute](#eventattribute)
- [Exceptions](#exceptions)
  - [StreamVersionConflictException](#streamversionconflictexception)
  - [StreamNotFoundException](#streamnotfoundexception)
- [Dependency Injection](#dependency-injection)
  - [ServiceCollectionExtensions](#servicecollectionextensions)

---

## Core Abstractions

### IEventStore

Primary interface for event sourcing operations.

```csharp
public interface IEventStore
{
    IAsyncEnumerable<StreamEvent> LoadAsync(
        StreamPointer fromVersion,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<StreamEvent> LoadAllAsync(
        long fromGlobalPosition = 0,
        string[]? streamTypeFilter = null,
        string[]? eventsFilter = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StreamEvent>> AppendAsync(
        StreamPointer expectedVersion,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default);
}
```

#### LoadAsync

Loads events from a specific stream starting from the specified version.

**Parameters:**
- `fromVersion` (StreamPointer): The stream pointer indicating where to start loading events
- `cancellationToken` (CancellationToken): Optional cancellation token

**Returns:** `IAsyncEnumerable<StreamEvent>` - Stream of events

**Example:**
```csharp
var streamId = new StreamIdentifier("Order", "order-123");
var pointer = new StreamPointer(streamId, version: 0);

await foreach (var streamEvent in eventStore.LoadAsync(pointer))
{
    Console.WriteLine($"Event at version {streamEvent.StreamPointer.Version}");
}
```

#### LoadAllAsync

Loads all events across all streams from a global position.

**Parameters:**
- `fromGlobalPosition` (long): The global position to start loading from. Defaults to 0 (beginning)
- `streamTypeFilter` (string[]?): Optional filter to include only specific stream types
- `eventsFilter` (string[]?): Optional filter to include only specific event types
- `cancellationToken` (CancellationToken): Optional cancellation token

**Returns:** `IAsyncEnumerable<StreamEvent>` - Stream of events from all matching streams

**Example:**
```csharp
// Load all Order events
await foreach (var streamEvent in eventStore.LoadAllAsync(
    fromGlobalPosition: 0,
    streamTypeFilter: new[] { "Order" }))
{
    // Process event
}
```

#### AppendAsync

Appends events to a stream with optimistic concurrency control.

**Parameters:**
- `expectedVersion` (StreamPointer): The expected stream version for optimistic concurrency. Use version 0 for new streams
- `events` (IReadOnlyList<AppendEvent>): The events to append to the stream
- `cancellationToken` (CancellationToken): Optional cancellation token

**Returns:** `Task<IReadOnlyList<StreamEvent>>` - The appended events with their assigned stream pointers and global positions

**Throws:**
- `StreamVersionConflictException`: When the expected version does not match the actual stream version
- `InvalidOperationException`: When event aggregate doesn't match stream type

**Example:**
```csharp
var streamId = new StreamIdentifier("Order", "order-123");
var pointer = new StreamPointer(streamId, version: 0); // New stream

var events = new[]
{
    new AppendEvent(
        new OrderCreatedEvent("order-123", 100m, "customer-1"),
        new[] { new AppendMetadata("CorrelationId", "abc-123") })
};

var result = await eventStore.AppendAsync(pointer, events);
// result[0].StreamPointer.Version == 1
```

---

### ISnapshotStore

Interface for persisting and retrieving stream snapshots.

```csharp
public interface ISnapshotStore
{
    Task<Snapshot?> LoadSnapshotAsync(
        StreamIdentifier streamIdentifier,
        CancellationToken cancellationToken = default);

    Task SaveSnapshotAsync(
        StreamPointer streamPointer,
        object state,
        CancellationToken cancellationToken = default);
}
```

#### LoadSnapshotAsync

Loads the most recent snapshot for a stream.

**Parameters:**
- `streamIdentifier` (StreamIdentifier): The identifier of the stream
- `cancellationToken` (CancellationToken): Optional cancellation token

**Returns:** `Task<Snapshot?>` - The snapshot if one exists, otherwise null

**Example:**
```csharp
var streamId = new StreamIdentifier("Order", "order-123");
var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);

if (snapshot != null)
{
    var state = (OrderState)snapshot.State;
    var version = snapshot.StreamPointer.Version;
}
```

#### SaveSnapshotAsync

Saves a snapshot of a stream's current state.

**Parameters:**
- `streamPointer` (StreamPointer): The stream pointer indicating the version being snapshotted
- `state` (object): The state to snapshot
- `cancellationToken` (CancellationToken): Optional cancellation token

**Returns:** `Task`

**Example:**
```csharp
var streamId = new StreamIdentifier("Order", "order-123");
var pointer = new StreamPointer(streamId, version: 100);
var state = new OrderState { /* ... */ };

await snapshotStore.SaveSnapshotAsync(pointer, state);
```

---

### IProjectionStore

Interface for persisting and retrieving projections.

```csharp
public interface IProjectionStore
{
    Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        CancellationToken cancellationToken = default);

    Task SaveProjectionAsync<TState>(
        string projectionKey,
        long globalPosition,
        TState state,
        CancellationToken cancellationToken = default);
}
```

#### LoadProjectionAsync

Loads a projection by its key.

**Type Parameters:**
- `TState`: The type of the projection state

**Parameters:**
- `projectionKey` (string): The unique key identifying the projection
- `cancellationToken` (CancellationToken): Optional cancellation token

**Returns:** `Task<Projection<TState>?>` - The projection if it exists, otherwise null

**Example:**
```csharp
var projection = await projectionStore
    .LoadProjectionAsync<OrderStatistics>("OrderStats");

if (projection != null)
{
    var stats = projection.State;
    var lastPosition = projection.GlobalPosition;
}
```

#### SaveProjectionAsync

Saves a projection with its current state and global position.

**Type Parameters:**
- `TState`: The type of the projection state

**Parameters:**
- `projectionKey` (string): The unique key identifying the projection
- `globalPosition` (long): The global position of the last processed event
- `state` (TState): The current state of the projection
- `cancellationToken` (CancellationToken): Optional cancellation token

**Returns:** `Task`

**Example:**
```csharp
var stats = new OrderStatistics { TotalOrders = 100, TotalRevenue = 10000m };
await projectionStore.SaveProjectionAsync("OrderStats", 500, stats);
```

---

## Data Types

### StreamIdentifier

Uniquely identifies a stream by its type and identifier.

```csharp
public sealed record StreamIdentifier(
    string StreamType,
    string Identifier);
```

**Properties:**
- `StreamType` (string): The type of the stream (e.g., "Order", "Customer")
- `Identifier` (string): The unique identifier within the stream type

**Implicit Conversion:**
```csharp
StreamIdentifier streamId = new("Order", "order-123");
StreamPointer pointer = streamId; // Implicitly converts to version 0
```

---

### StreamPointer

Represents a pointer to a specific version within a stream.

```csharp
public sealed record StreamPointer(
    StreamIdentifier Stream,
    long Version);
```

**Properties:**
- `Stream` (StreamIdentifier): The stream identifier
- `Version` (long): The version number within the stream. Version 0 indicates a new stream

**Example:**
```csharp
var streamId = new StreamIdentifier("Order", "order-123");
var pointer = new StreamPointer(streamId, version: 5);
```

---

### AppendEvent

Represents an event to be appended to a stream.

```csharp
public sealed record AppendEvent(
    object Event,
    IReadOnlyList<AppendMetadata>? Metadata = null);
```

**Properties:**
- `Event` (object): The event data to append
- `Metadata` (IReadOnlyList<AppendMetadata>?): Optional client metadata. System automatically adds Source="Client"

---

### StreamEvent

Represents an event that has been persisted to a stream.

```csharp
public sealed record StreamEvent(
    StreamPointer StreamPointer,
    object Event,
    IReadOnlyList<EventMetadata> Metadata);
```

**Properties:**
- `StreamPointer` (StreamPointer): The stream pointer indicating the stream and version
- `Event` (object): The event data
- `Metadata` (IReadOnlyList<EventMetadata>): The source-tracked metadata

---

### AppendMetadata

Client-provided metadata for events (no Source field - prevents spoofing).

```csharp
public sealed record AppendMetadata(
    string Key,
    object? Value);
```

**Properties:**
- `Key` (string): The metadata key (e.g., "CorrelationId", "UserId")
- `Value` (object?): The metadata value

---

### EventMetadata

Stored metadata with source tracking.

```csharp
public sealed record EventMetadata(
    string Source,
    string Key,
    object? Value);
```

**Properties:**
- `Source` (string): The source of the metadata ("Client", "System", "Application")
- `Key` (string): The metadata key
- `Value` (object?): The metadata value

---

### Snapshot

Represents a snapshot of a stream's state at a specific version.

```csharp
public sealed record Snapshot(
    StreamPointer StreamPointer,
    object State);
```

**Properties:**
- `StreamPointer` (StreamPointer): The stream pointer indicating the stream and version
- `State` (object): The snapshotted state

---

### Projection

Represents a projection's state at a specific global position.

```csharp
public sealed record Projection<TState>(
    TState State,
    long GlobalPosition);
```

**Type Parameters:**
- `TState`: The type of the projection state

**Properties:**
- `State` (TState): The current state of the projection
- `GlobalPosition` (long): The global position of the last processed event

---

## Attributes

### EventAttribute

Decorates an event class with metadata for serialization and versioning.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EventAttribute(
    string aggregate,
    string name,
    int version) : Attribute
```

**Parameters:**
- `aggregate` (string): The aggregate type this event belongs to
- `name` (string): The name of the event
- `version` (int): The version of the event schema

**Properties:**
- `Aggregate` (string): Gets the aggregate type
- `Name` (string): Gets the event name
- `Version` (int): Gets the schema version

**Example:**
```csharp
[Event("Order", "Created", 1)]
public record OrderCreatedEvent(string OrderId, decimal Amount);
```

---

## Exceptions

### StreamVersionConflictException

Thrown when the expected stream version doesn't match the actual version (optimistic concurrency failure).

```csharp
public class StreamVersionConflictException : Exception
{
    public StreamPointer? ExpectedVersion { get; init; }
    public StreamPointer? ActualVersion { get; init; }
}
```

**Properties:**
- `ExpectedVersion` (StreamPointer?): The version that was expected
- `ActualVersion` (StreamPointer?): The actual current version

---

### StreamNotFoundException

Thrown when attempting to load a stream that doesn't exist.

```csharp
public class StreamNotFoundException : Exception
{
    public StreamIdentifier? StreamIdentifier { get; init; }
}
```

**Properties:**
- `StreamIdentifier` (StreamIdentifier?): The stream that was not found

---

## Dependency Injection

### ServiceCollectionExtensions

Extension methods for configuring Event Store services.

#### AddEventStore

Registers all stores (IEventStore, ISnapshotStore, IProjectionStore) with shared DbContext.

```csharp
public static IServiceCollection AddEventStore(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> optionsAction)
```

#### AddEventStoreInMemory

Registers all stores with in-memory database (testing).

```csharp
public static IServiceCollection AddEventStoreInMemory(
    this IServiceCollection services,
    string databaseName)
```

#### AddEventStoreSqlServer

Registers all stores with SQL Server.

```csharp
public static IServiceCollection AddEventStoreSqlServer(
    this IServiceCollection services,
    string connectionString)
```

#### AddEventStoreOnly

Registers only IEventStore with separate configuration.

```csharp
public static IServiceCollection AddEventStoreOnly(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> optionsAction,
    ServiceLifetime lifetime = ServiceLifetime.Scoped)
```

#### AddSnapshotStoreOnly

Registers only ISnapshotStore with separate configuration.

```csharp
public static IServiceCollection AddSnapshotStoreOnly(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> optionsAction,
    ServiceLifetime lifetime = ServiceLifetime.Scoped)
```

#### AddProjectionStoreOnly

Registers only IProjectionStore with separate configuration.

```csharp
public static IServiceCollection AddProjectionStoreOnly(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> optionsAction,
    ServiceLifetime lifetime = ServiceLifetime.Scoped)
```

---

## Version History

- **1.0.0** - Initial release with core event sourcing, snapshots, and projections

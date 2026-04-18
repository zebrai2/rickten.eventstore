# API Reference

Complete API documentation for Rickten.EventStore.

## EF Core Provider Support

**Rickten.EventStore.EntityFramework includes:**
- ? **SQL Server** - Bundled with first-class `AddEventStoreSqlServer()` helper methods
- ? **InMemory** - Bundled with first-class `AddEventStoreInMemory()` helper methods for testing

**Other EF Core providers** (PostgreSQL, SQLite, MySQL, Oracle, etc.) are supported through the generic `AddEventStore(options => ...)` method. To use these providers:
1. Install the provider package in your application (e.g., `Npgsql.EntityFrameworkCore.PostgreSQL` or `Microsoft.EntityFrameworkCore.Sqlite`)
2. Configure using `AddEventStore()` with the provider's `Use*()` method (see [AddEventStore](#addeventstore) examples)

Rickten does not bundle these providers or provide dedicated helper methods like `AddEventStorePostgres()` or `AddEventStoreSqlite()`.

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

Loads events from a specific stream starting **after** the specified version.

**Behavior:** Events are loaded exclusively - if `fromVersion.Version` is N, only events with version > N are returned.

**Parameters:**
- `fromVersion` (StreamPointer): The stream pointer indicating the version to start loading after (exclusive)
- `cancellationToken` (CancellationToken): Optional cancellation token

**Returns:** `IAsyncEnumerable<StreamEvent>` - Stream of events

**Example:**
```csharp
var streamId = new StreamIdentifier("Order", "order-123");
var pointer = new StreamPointer(streamId, version: 0); // Load all events (version > 0)

await foreach (var streamEvent in eventStore.LoadAsync(pointer))
{
    Console.WriteLine($"Event at version {streamEvent.StreamPointer.Version}");
}
```

**Note:** To load from the beginning, use `version: 0`. To resume after a snapshot at version N, use `version: N` to load events starting from version N+1.


#### LoadAllAsync

Loads all events across all streams starting **after** a global position.

**Behavior:** Events are loaded exclusively - if `fromGlobalPosition` is N, only events with global position > N are returned. This matches the behavior of `LoadAsync`.

**Parameters:**
- `fromGlobalPosition` (long): The global position to start loading after (exclusive). Defaults to 0 (beginning)
- `streamTypeFilter` (string[]?): Optional filter to include only specific stream types (aggregate types)
- `eventsFilter` (string[]?): Optional filter to include only specific event types using **wire-name format**: `{Aggregate}.{Name}.v{Version}`. Wire names must match the `[Event]` attribute values, not short event names.
- `cancellationToken` (CancellationToken): Optional cancellation token

**Returns:** `IAsyncEnumerable<StreamEvent>` - Stream of events from all matching streams

**Example:**
```csharp
// Load all Order events from the beginning (position > 0)
await foreach (var streamEvent in eventStore.LoadAllAsync(
    fromGlobalPosition: 0,
    streamTypeFilter: new[] { "Order" }))
{
    // Process event
}

// Filter by specific event types using wire names
// For [Event("Order", "Created", 1)] use "Order.Created.v1"
await foreach (var streamEvent in eventStore.LoadAllAsync(
    fromGlobalPosition: 0,
    eventsFilter: new[] { "Order.Created.v1", "Order.Paid.v1" }))
{
    // Process only OrderCreatedEvent and OrderPaidEvent
}

// Resume from a checkpoint at position N (loads events with position > N)
await foreach (var streamEvent in eventStore.LoadAllAsync(
    fromGlobalPosition: lastCheckpointPosition,
    streamTypeFilter: new[] { "Order" }))
{
    // Process new events
}
```

**Note:** To load from the beginning, use `fromGlobalPosition: 0`. To resume from a checkpoint at position N, use `fromGlobalPosition: N` to load events starting from position N+1.

#### AppendAsync

Appends events to a stream with optimistic concurrency control.

**Parameters:**
- `expectedVersion` (StreamPointer): The expected **current stream version** for optimistic concurrency. Version 0 for new streams (no events written yet). If the stream has 5 events, pass version 5 to append after the last event. The new events will be written starting at version + 1.
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

// To append more events, pass the current version
var moreEvents = new[] { new AppendEvent(new OrderShippedEvent("tracking-123")) };
var pointer2 = new StreamPointer(streamId, version: 1); // Stream now has 1 event
var result2 = await eventStore.AppendAsync(pointer2, moreEvents);
// result2[0].StreamPointer.Version == 2
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
- `Version` (long): The current stream version (last written event version). Version 0 indicates a new stream with no events. Version N means the stream has N events, and the next append will write version N+1.

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
    long GlobalPosition,
    object Event,
    IReadOnlyList<EventMetadata> Metadata);
```

**Properties:**
- `StreamPointer` (StreamPointer): The stream pointer indicating the stream and version
- `GlobalPosition` (long): The global position of this event across all streams
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

**Important: Value Type After Storage**

After round-trip through storage, non-null `Value` instances materialize as `System.Text.Json.JsonElement` rather than their original CLR types due to JSON serialization. Use the provided extension methods for safe, typed access:

```csharp
// Use extension methods on IReadOnlyList<EventMetadata>
var timestamp = metadata.GetDateTime("Timestamp");
var userId = metadata.GetString("UserId");
var correlationId = metadata.GetGuid("CorrelationId");

// Available: GetString, GetDateTime, GetGuid, GetInt32, GetInt64, 
//            GetDecimal, GetDouble, GetBoolean
```

**Do not use direct casts** - they will fail after storage:
```csharp
// Avoid: This will fail after round-trip through storage
var timestamp = metadata.First(m => m.Key == "Timestamp").Value as DateTime?;

// Recommended: Use the typed extension method instead
var timestamp = metadata.GetDateTime("Timestamp");
```

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

**Wire Name Format:** The event store generates a wire name for each event type using the format: `{Aggregate}.{Name}.v{Version}`. This wire name is used for event type filtering in `IEventStore.LoadAllAsync` and projection filters. For example, `[Event("Order", "Created", 1)]` produces the wire name `"Order.Created.v1"`.

**Example:**
```csharp
[Event("Order", "Created", 1)]
public record OrderCreatedEvent(string OrderId, decimal Amount);
// Wire name: "Order.Created.v1"

[Event("Order", "Paid", 1)]
public record OrderPaidEvent(string PaymentId, decimal Amount);
// Wire name: "Order.Paid.v1"

// Use wire names when filtering:
await foreach (var evt in eventStore.LoadAllAsync(
    eventsFilter: new[] { "Order.Created.v1", "Order.Paid.v1" }))
{
    // Process only OrderCreatedEvent and OrderPaidEvent
}
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

**Primary Overload (Assembly Array):**
```csharp
public static IServiceCollection AddEventStore(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> optionsAction,
    params Assembly[] assemblies)
```

**Parameters:**
- `services` (IServiceCollection): The service collection to add services to
- `optionsAction` (Action<DbContextOptionsBuilder>): An action to configure the DbContext
- `assemblies` (params Assembly[]): The assemblies containing events, aggregates, projections, commands, and reactions. **Required** - at least one assembly must be provided.

**Throws:**
- `ArgumentException`: When no assemblies are provided

**Example:**
```csharp
// SQL Server (bundled provider)
services.AddEventStore(options =>
{
    options.UseSqlServer(connectionString);
}, typeof(MyEvent).Assembly, typeof(MyAggregate).Assembly);

// PostgreSQL (requires Npgsql.EntityFrameworkCore.PostgreSQL package)
services.AddEventStore(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(5);
    });
}, typeof(MyEvent).Assembly);

// SQLite (requires Microsoft.EntityFrameworkCore.Sqlite package)
services.AddEventStore(options =>
{
    options.UseSqlite("Data Source=eventstore.db");
}, typeof(MyEvent).Assembly);
```

**Marker-Type Overloads:**

Use a type from the assembly to scan:
```csharp
public static IServiceCollection AddEventStore<TMarker>(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> optionsAction)
```

Use two types from different assemblies:
```csharp
public static IServiceCollection AddEventStore<TMarker1, TMarker2>(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> optionsAction)
```

Use three types from different assemblies:
```csharp
public static IServiceCollection AddEventStore<TMarker1, TMarker2, TMarker3>(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> optionsAction)
```

**Example:**
```csharp
// Single assembly using marker type
services.AddEventStore<OrderCreatedEvent>(options =>
{
    options.UseSqlServer(connectionString);
});

// PostgreSQL example (requires Npgsql.EntityFrameworkCore.PostgreSQL package)
services.AddEventStore<OrderCreatedEvent>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(5);
    });
});

// SQLite example (requires Microsoft.EntityFrameworkCore.Sqlite package)
services.AddEventStore<OrderCreatedEvent>(options =>
{
    options.UseSqlite("Data Source=eventstore.db");
});

// Multiple assemblies using marker types
services.AddEventStore<OrderCreatedEvent, CustomerAggregate>(options =>
{
    options.UseSqlServer(connectionString);
});
```

#### AddEventStoreInMemory

Registers all stores with in-memory database using the bundled provider (for testing).

**Primary Overload (Assembly Array):**
```csharp
public static IServiceCollection AddEventStoreInMemory(
    this IServiceCollection services,
    string databaseName,
    params Assembly[] assemblies)
```

**Parameters:**
- `services` (IServiceCollection): The service collection to add services to
- `databaseName` (string): The name of the in-memory database
- `assemblies` (params Assembly[]): The assemblies containing events, aggregates, projections, commands, and reactions. **Required** - at least one assembly must be provided.

**Throws:**
- `ArgumentException`: When no assemblies are provided

**Example:**
```csharp
services.AddEventStoreInMemory(
    "TestDb",
    typeof(MyEvent).Assembly,
    typeof(MyAggregate).Assembly);
```

**Marker-Type Overloads:**

Use a type from the assembly to scan:
```csharp
public static IServiceCollection AddEventStoreInMemory<TMarker>(
    this IServiceCollection services,
    string databaseName)
```

Use two types from different assemblies:
```csharp
public static IServiceCollection AddEventStoreInMemory<TMarker1, TMarker2>(
    this IServiceCollection services,
    string databaseName)
```

Use three types from different assemblies:
```csharp
public static IServiceCollection AddEventStoreInMemory<TMarker1, TMarker2, TMarker3>(
    this IServiceCollection services,
    string databaseName)
```

**Example:**
```csharp
// Single assembly using marker type
services.AddEventStoreInMemory<OrderCreatedEvent>("TestDb");

// Multiple assemblies using marker types
services.AddEventStoreInMemory<OrderCreatedEvent, CustomerAggregate>("TestDb");
```

#### AddEventStoreSqlServer

Registers all stores with SQL Server using the bundled provider.

**Primary Overload (Assembly Array):**
```csharp
public static IServiceCollection AddEventStoreSqlServer(
    this IServiceCollection services,
    string connectionString,
    Assembly[] assemblies,
    Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
```

**Parameters:**
- `services` (IServiceCollection): The service collection to add services to
- `connectionString` (string): The SQL Server connection string
- `assemblies` (Assembly[]): The assemblies containing events, aggregates, projections, commands, and reactions. **Required** - at least one assembly must be provided.
- `sqlServerOptionsAction` (Action<SqlServerDbContextOptionsBuilder>?): Optional SQL Server specific configuration (e.g., retry policies)

**Throws:**
- `ArgumentException`: When no assemblies are provided

**Example:**
```csharp
services.AddEventStoreSqlServer(
    connectionString,
    new[] { typeof(MyEvent).Assembly, typeof(MyAggregate).Assembly },
    sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(5);
        sqlOptions.CommandTimeout(60);
    });
```

**Marker-Type Overloads:**

Use a type from the assembly to scan:
```csharp
public static IServiceCollection AddEventStoreSqlServer<TMarker>(
    this IServiceCollection services,
    string connectionString,
    Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
```

Use two types from different assemblies:
```csharp
public static IServiceCollection AddEventStoreSqlServer<TMarker1, TMarker2>(
    this IServiceCollection services,
    string connectionString,
    Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
```

Use three types from different assemblies:
```csharp
public static IServiceCollection AddEventStoreSqlServer<TMarker1, TMarker2, TMarker3>(
    this IServiceCollection services,
    string connectionString,
    Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
```

**Example:**
```csharp
// Single assembly using marker type
services.AddEventStoreSqlServer<OrderCreatedEvent>(
    connectionString,
    sqlOptions => sqlOptions.EnableRetryOnFailure(5));

// Multiple assemblies using marker types
services.AddEventStoreSqlServer<OrderCreatedEvent, CustomerAggregate>(
    connectionString);
```

---

## Version History

- **1.0.0** - Initial release with core event sourcing, snapshots, and projections

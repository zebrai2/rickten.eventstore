# Architecture Guide

This document explains the architectural decisions and design patterns used in Rickten.EventStore.

## Table of Contents

- [Overview](#overview)
- [Core Principles](#core-principles)
- [Architectural Layers](#architectural-layers)
- [Key Design Decisions](#key-design-decisions)
- [Security Model](#security-model)
- [Concurrency Control](#concurrency-control)
- [Serialization Strategy](#serialization-strategy)
- [Database Schema](#database-schema)
- [Extension Points](#extension-points)

---

## Overview

Rickten.EventStore is a lightweight, flexible Event Sourcing library designed with the following goals:

- **Simplicity**: Easy to understand and use
- **Flexibility**: Support multiple storage backends
- **Security**: Prevent metadata spoofing and ensure data integrity
- **Performance**: Optimize for both read and write operations
- **Testability**: Easy to unit test without external dependencies

---

## Core Principles

### 1. Separation of Concerns

The library is split into clear layers:

```
┌──────────────────────────────────────┐
│         Application Layer            │  (Your domain logic)
├──────────────────────────────────────┤
│    Rickten.EventStore (Core)         │  (Abstractions only)
├──────────────────────────────────────┤
│  Rickten.EventStore.EntityFramework  │  (Implementation)
└──────────────────────────────────────┘
```

**Benefits:**
- Core package has no dependencies (pure abstractions)
- Easy to swap implementations
- Testable without infrastructure

### 2. Interface-Driven Design

All functionality is exposed through interfaces:

- `IEventStore` - Event operations
- `ISnapshotStore` - Snapshot operations  
- `IProjectionStore` - Projection operations

**Benefits:**
- Easy to mock for testing
- Supports multiple implementations
- Follows Dependency Inversion Principle

### 3. Immutability

All data types are immutable records:

```csharp
public sealed record StreamEvent(
    StreamPointer StreamPointer,
    object Event,
    IReadOnlyList<EventMetadata> Metadata);
```

**Benefits:**
- Thread-safe by default
- Easier to reason about
- Prevents accidental mutations

---

## Architectural Layers

### Layer 1: Core Abstractions (Rickten.EventStore)

**Purpose:** Define contracts without implementation details

**Contains:**
- Interfaces (`IEventStore`, `ISnapshotStore`, `IProjectionStore`)
- Data types (`StreamIdentifier`, `StreamPointer`, `AppendEvent`, etc.)
- Exceptions (`StreamVersionConflictException`, etc.)
- Attributes (`EventAttribute`)

**Dependencies:** None (zero dependencies)

**Example:**
```csharp
namespace Rickten.EventStore;

public interface IEventStore
{
    Task<IReadOnlyList<StreamEvent>> AppendAsync(...);
    IAsyncEnumerable<StreamEvent> LoadAsync(...);
}
```

### Layer 2: Entity Framework Implementation (Rickten.EventStore.EntityFramework)

**Purpose:** Provide concrete implementation using EF Core

**Contains:**
- `EventStore` - Implements `IEventStore`
- `SnapshotStore` - Implements `ISnapshotStore`
- `ProjectionStore` - Implements `IProjectionStore`
- `EventStoreDbContext` - EF Core context
- Entity types (`EventEntity`, `SnapshotEntity`, `ProjectionEntity`)
- Serialization logic

**Dependencies:**
- `Rickten.EventStore` (core)
- `Microsoft.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.Relational`

### Layer 3: Application Layer (Your Code)

**Purpose:** Implement domain logic using the event store

**Contains:**
- Aggregate roots
- Command handlers
- Event handlers
- Projection builders
- Domain events

---

## Key Design Decisions

### 1. Streams as First-Class Citizens

Events are organized into streams identified by type and ID:

```csharp
var streamId = new StreamIdentifier(
    streamType: "Order",      // Aggregate type
    identifier: "order-123"); // Instance ID
```

**Rationale:**
- Natural mapping to Domain-Driven Design aggregates
- Easy to query events for a specific aggregate
- Supports optimistic concurrency per stream

### 2. Optimistic Concurrency Control

Events are appended with an expected version:

```csharp
var pointer = new StreamPointer(streamId, expectedVersion: 5);
await eventStore.AppendAsync(pointer, events);
```

**Rationale:**
- Prevents lost updates in concurrent scenarios
- Simple to implement and understand
- Standard pattern in event sourcing

**Implementation:**
- Unique constraint on `(StreamType, StreamIdentifier, Version)`
- Database enforces atomicity
- Clear exception on conflict

### 3. Global Ordering

All events have a global position for cross-stream ordering:

```csharp
await foreach (var e in eventStore.LoadAllAsync(fromGlobalPosition: 100))
{
    // Events ordered globally
}
```

**Rationale:**
- Enables projections across multiple streams
- Supports catch-up subscriptions
- Maintains causality ordering

**Implementation:**
- Auto-incrementing `GlobalPosition` column
- Indexed for fast range queries
- Never gaps in sequence

### 4. Separate Metadata Types

Different metadata types for append vs storage:

```csharp
// Client provides AppendMetadata (no Source)
new AppendMetadata("CorrelationId", "abc-123")

// System stores EventMetadata (with Source)
new EventMetadata("Client", "CorrelationId", "abc-123")
```

**Rationale:**
- **Security**: Prevents clients from spoofing system metadata
- **Clarity**: Clear separation of client vs system metadata
- **Auditability**: System always adds timestamp, version, etc.

**Benefits:**
- Clients cannot inject `Source="System"`
- Type safety enforced at compile time
- Automatic system metadata injection

### 5. Snapshots as Optimization

Snapshots are optional and separate from events:

```csharp
// Optional: Save snapshot every 100 events
if (version % 100 == 0)
{
    await snapshotStore.SaveSnapshotAsync(pointer, state);
}
```

**Rationale:**
- Events are the source of truth (never snapshots)
- Snapshots are pure performance optimization
- Rebuilding from events always works

**Trade-offs:**
- ✅ Fast aggregate rebuilding
- ❌ Requires serialization of state
- ❌ Extra storage cost

### 6. Projections with Position Tracking

Projections track the last processed global position:

```csharp
public record Projection<TState>(
    TState State,
    long GlobalPosition);
```

**Rationale:**
- Enables incremental updates
- Supports crash recovery
- Allows multiple projections at different positions

**Pattern:**
```csharp
var projection = await projectionStore.LoadProjectionAsync<Stats>("Stats");
var lastPosition = projection?.GlobalPosition ?? 0;

await foreach (var e in eventStore.LoadAllAsync(lastPosition))
{
    state = state.Apply(e.Event);
    await projectionStore.SaveProjectionAsync("Stats", e.GlobalPosition, state);
}
```

---

## Security Model

### Metadata Source Tracking

**Problem:** Clients could inject system metadata

**Solution:** Separate types with automatic transformation

```
Client Code                          Event Store
───────────                          ───────────
AppendMetadata                  →    EventMetadata
(Key, Value)                         (Source="Client", Key, Value)
                                     + EventMetadata("System", "Timestamp", ...)
```

**Security Properties:**
- ✅ Clients cannot set `Source`
- ✅ System metadata always accurate
- ✅ Type-safe at compile time
- ✅ Tamper-proof audit trail

### Event Aggregate Validation

Events are validated against the stream type:

```csharp
[Event("Order", "Created", 1)]
public record OrderCreatedEvent(...);

// ✅ OK: Order event to Order stream
await eventStore.AppendAsync(
    new StreamPointer(new StreamIdentifier("Order", "1"), 0),
    new[] { new AppendEvent(new OrderCreatedEvent(...), null) });

// ❌ Error: Invoice event to Order stream
await eventStore.AppendAsync(
    new StreamPointer(new StreamIdentifier("Order", "1"), 0),
    new[] { new AppendEvent(new InvoiceCreatedEvent(...), null) });
// Throws: InvalidOperationException
```

**Benefits:**
- Prevents accidental cross-stream pollution
- Maintains aggregate boundaries
- Clear error messages

---

## Concurrency Control

### Optimistic Locking

Every append specifies the expected version:

```csharp
// Process 1                           // Process 2
var pointer = new StreamPointer(       var pointer = new StreamPointer(
    streamId, version: 5);                 streamId, version: 5);
                                       
await eventStore.AppendAsync(          // Wait...
    pointer, events);                  
// Success! Now at version 6          
                                       await eventStore.AppendAsync(
                                           pointer, events);
                                       // Throws StreamVersionConflictException!
```

**Conflict Resolution:**
1. Catch `StreamVersionConflictException`
2. Reload current state
3. Re-apply command
4. Retry append with new version

**Example:**
```csharp
for (int retry = 0; retry < maxRetries; retry++)
{
    try
    {
        var state = await LoadCurrentStateAsync(streamId);
        var events = command.Execute(state);
        var pointer = new StreamPointer(streamId, state.Version);
        await eventStore.AppendAsync(pointer, events);
        break; // Success!
    }
    catch (StreamVersionConflictException)
    {
        if (retry == maxRetries - 1) throw;
        await Task.Delay(100); // Brief pause
    }
}
```

### Database-Level Guarantees

**Unique Constraint:**
```sql
CREATE UNIQUE INDEX IX_Events_Stream_Version 
ON Events (StreamType, StreamIdentifier, Version);
```

**Benefits:**
- Database enforces atomicity
- No race conditions possible
- Works across processes/servers

---

## Serialization Strategy

### JSON with Type Information

Events are serialized as JSON with type names:

```json
{
  "eventType": "MyApp.Orders.OrderCreatedEvent, MyApp",
  "eventData": {
    "orderId": "order-123",
    "amount": 100.00
  }
}
```

**Type Resolution:**
- Event type stored separately in `EventType` column
- Deserialization uses reflection to load type
- Supports versioning through `EventAttribute`

### Metadata Serialization

Metadata is serialized as JSON array:

```json
[
  {
    "source": "Client",
    "key": "CorrelationId",
    "value": "abc-123"
  },
  {
    "source": "System",
    "key": "Timestamp",
    "value": "2025-01-18T10:30:00Z"
  }
]
```

### Snapshot Serialization

Snapshots store state as JSON with type info:

```json
{
  "stateType": "MyApp.Orders.OrderState, MyApp",
  "state": {
    "orderId": "order-123",
    "status": "Completed",
    "total": 100.00
  }
}
```

---

## Database Schema

### Events Table

```sql
CREATE TABLE Events (
    Id BIGINT PRIMARY KEY IDENTITY,
    StreamType NVARCHAR(255) NOT NULL,
    StreamIdentifier NVARCHAR(255) NOT NULL,
    Version BIGINT NOT NULL,
    EventType NVARCHAR(255) NOT NULL,
    EventData NVARCHAR(MAX) NOT NULL,
    Metadata NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT UQ_Events_Stream_Version 
        UNIQUE (StreamType, StreamIdentifier, Version)
);

CREATE INDEX IX_Events_GlobalPosition ON Events (Id);
CREATE INDEX IX_Events_Stream ON Events (StreamType, StreamIdentifier);
```

**Design Rationale:**
- `Id` - Auto-incrementing primary key that serves as the global position across all streams
- `(StreamType, StreamIdentifier, Version)` - Unique constraint for optimistic concurrency control
- Indexes support both stream-specific queries and global ordering queries
- Single IDENTITY column ensures strict sequential ordering of events

### Snapshots Table

```sql
CREATE TABLE Snapshots (
    StreamType NVARCHAR(255) NOT NULL,
    StreamIdentifier NVARCHAR(255) NOT NULL,
    Version BIGINT NOT NULL,
    StateType NVARCHAR(255) NOT NULL,
    State NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    
    PRIMARY KEY (StreamType, StreamIdentifier)
);
```

**Design Rationale:**
- Primary key on stream ensures one snapshot per stream
- Overwrites previous snapshot (optimization)
- Includes version for resuming from snapshot

### Projections Table

```sql
CREATE TABLE Projections (
    ProjectionKey NVARCHAR(255) PRIMARY KEY,
    GlobalPosition BIGINT NOT NULL,
    State NVARCHAR(MAX) NOT NULL,
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
```

**Design Rationale:**
- Key-based lookup for fast queries
- GlobalPosition tracks progress
- State stored as JSON for flexibility

---

## Extension Points

### Custom Storage Backends

Implement the core interfaces for other databases:

```csharp
public class CosmosEventStore : IEventStore
{
    // Implement using Azure Cosmos DB
}

services.AddScoped<IEventStore, CosmosEventStore>();
```

### Custom Serialization

Override serialization for specific needs:

```csharp
public class ProtobufSerializer : IEventSerializer
{
    // Use Protocol Buffers instead of JSON
}
```

### Custom Metadata Injection

Add application-specific metadata:

```csharp
public class ApplicationEventStore : IEventStore
{
    private readonly IEventStore _inner;
    
    public async Task<IReadOnlyList<StreamEvent>> AppendAsync(
        StreamPointer expectedVersion,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default)
    {
        // Add application metadata here before delegating
        var enrichedEvents = events.Select(e => new AppendEvent(
            e.Event,
            e.Metadata.Concat(new[]
            {
                new AppendMetadata("TenantId", GetCurrentTenantId()),
                new AppendMetadata("ApplicationVersion", "1.2.3")
            }).ToList()));
        
        return await _inner.AppendAsync(expectedVersion, enrichedEvents, cancellationToken);
    }
}
```

---

## Performance Considerations

### Read Optimization

- **Snapshots**: Reduce events to replay
- **Indexes**: Fast stream and global position queries
- **Streaming**: Use `IAsyncEnumerable` to avoid loading all events

### Write Optimization

- **Batch Appends**: Append multiple events in one transaction
- **Connection Pooling**: EF Core manages connections efficiently
- **Async All The Way**: Non-blocking I/O operations

### Scalability

- **Read Replicas**: Use EF Core read-only connections for queries
- **Separate Databases**: Different databases for events, snapshots, projections
- **Sharding**: Partition by stream type or identifier

---

## Future Enhancements

Potential areas for expansion:

- **Subscriptions**: Real-time event notifications
- **Event Upcasting**: Automatic schema migration
- **Encryption**: At-rest and in-transit encryption
- **Compression**: Compress event data
- **TTL/Archiving**: Archive old events
- **Additional Providers**: DynamoDB, MongoDB, etc.

---

## Conclusion

Rickten.EventStore is designed to be simple, secure, and flexible. The architecture supports:

- ✅ Clean separation of concerns
- ✅ Multiple storage backends
- ✅ Security by design
- ✅ Optimistic concurrency
- ✅ Performance optimization
- ✅ Easy testing

For more details, see:
- [API Reference](API.md)
- [Performance Guide](PERFORMANCE.md)
- [Migration Guide](MIGRATION.md)

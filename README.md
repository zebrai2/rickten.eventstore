# Rickten.EventStore

A lightweight, flexible **Event Sourcing** library for .NET with Entity Framework Core support. Store events, snapshots, and projections with built-in optimistic concurrency control and dependency injection support.

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![C# 14.0](https://img.shields.io/badge/C%23-14.0-blue)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![Entity Framework Core](https://img.shields.io/badge/EF%20Core-10.0-green)](https://docs.microsoft.com/en-us/ef/core/)

## 📦 Packages

This repository contains four NuGet packages:

### **Rickten.EventStore**
Core event sourcing abstractions and contracts. Provides `IEventStore`, `ISnapshotStore`, and `IProjectionStore` interfaces.

```bash
dotnet add package Rickten.EventStore
```

### **Rickten.EventStore.EntityFramework**
Entity Framework Core implementation of the event store. **Includes SQL Server and InMemory providers.** Supports other EF Core providers (PostgreSQL, SQLite, etc.) through generic `DbContextOptions` configuration when you install the provider package separately.

```bash
dotnet add package Rickten.EventStore.EntityFramework
```

### **Rickten.Aggregator**
Lightweight library for implementing event-sourced aggregates (write-side) with clean separation between state folding and command decision-making. Features strict-by-default validation and declarative snapshot configuration.

```bash
dotnet add package Rickten.Aggregator
```

[📖 Read the Aggregator documentation →](Rickten.Aggregator/README.md)

### **Rickten.Projector**
Lightweight library for building event-sourced projections (read-side/read models) with declarative filtering and checkpoint management.

```bash
dotnet add package Rickten.Projector
```

[📖 Read the Projector documentation →](Rickten.Projector/README.md)

## 📋 Table of Contents

- [Features](#-features)
- [Installation](#-installation)
- [Quick Start](#-quick-start)
- [Core Concepts](#-core-concepts)
- [Dependency Injection Setup](#-dependency-injection-setup)
- [Working with Events](#-working-with-events)
- [Snapshots](#-snapshots)
- [Projections](#-projections)
- [Advanced Scenarios](#-advanced-scenarios)
- [Database Configuration](#-database-configuration)
- [Testing](#-testing)
- [Examples](#-examples)
- [Best Practices](#-best-practices)

## 🚀 Features

- ✅ **Event Sourcing** - Store and retrieve domain events with full history
- ✅ **Optimistic Concurrency** - Built-in version conflict detection
- ✅ **Snapshots** - Optimize aggregate rebuilding with state snapshots
- ✅ **Projections** - Build read models from event streams
- ✅ **Dependency Injection** - First-class DI support with flexible configuration
- ✅ **Multiple Storage Options** - SQL Server and InMemory included; PostgreSQL, SQLite, and other EF Core providers supported via generic configuration
- ✅ **Async/Await** - Modern async patterns with `IAsyncEnumerable`
- ✅ **Strong Typing** - Type-safe event handling with records
- ✅ **Metadata Support** - Attach metadata to events (correlation IDs, causation IDs, etc.)
- ✅ **Aggregate Pattern Support** - Clean command/event/state separation with Rickten.Aggregator

## 📦 Installation

```bash
# Install the core library and EF Core provider
dotnet add package Rickten.EventStore.EntityFramework

# Optional: Add aggregator support
dotnet add package Rickten.Aggregator

# For database providers:
# - SQL Server and InMemory providers are included in Rickten.EventStore.EntityFramework
# - For other databases, install the provider package separately:
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL  # For PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Sqlite   # For SQLite
```

### ⚠️ Type Registration Requirement

**You must explicitly register assemblies** containing your events, aggregates, projections, commands, and reactions. The event store uses a **type metadata registry** built at startup for efficient type resolution without runtime assembly scanning.

```csharp
// Register assemblies when configuring the event store
services.AddEventStoreSqlServer(
    connectionString,
    new[] { typeof(MyEvent).Assembly, typeof(MyAggregate).Assembly });

// Or use a marker-type overload
services.AddEventStoreSqlServer<MyEvent>(connectionString);

// For testing with in-memory database
services.AddEventStoreInMemory(
    "TestDb",
    typeof(MyEvent).Assembly);

// Or use marker-type overload for testing
services.AddEventStoreInMemory<MyEvent>("TestDb");
```

**Important:** If assemblies are not specified, the event store will throw an `ArgumentException`. There is no default or calling-assembly fallback.

## ⚡ Quick Start

### 1. Define Your Events

```csharp
using Rickten.EventStore;

[Event("Order", "Created", 1)]
public record OrderCreatedEvent(
    string OrderId,
    decimal Amount,
    string CustomerId);

[Event("Order", "Paid", 1)]
public record OrderPaidEvent(
    string PaymentId,
    decimal Amount);

[Event("Order", "Shipped", 1)]
public record OrderShippedEvent(
    string TrackingNumber);
```

### 2. Configure Dependency Injection

```csharp
using Rickten.EventStore.EntityFramework;

var builder = WebApplication.CreateBuilder(args);

// Register all stores together (events, snapshots, and projections share a DbContext)
// Pass assemblies containing your events, aggregates, and commands
builder.Services.AddEventStoreSqlServer(
    builder.Configuration.GetConnectionString("EventStore"),
    new[] { typeof(OrderCreatedEvent).Assembly });  // Register assemblies with event types

// Alternative: Use generic marker types to identify assemblies
builder.Services.AddEventStoreSqlServer<OrderCreatedEvent>(
    builder.Configuration.GetConnectionString("EventStore"));

var app = builder.Build();
```

### 3. Use the Event Store

```csharp
using Rickten.EventStore;

public class OrderService
{
    private readonly IEventStore _eventStore;

    public OrderService(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<string> CreateOrderAsync(decimal amount, string customerId)
    {
        var orderId = Guid.NewGuid().ToString();
        
        // Create a pointer to a new stream (version 0)
        var pointer = new StreamPointer(
            new StreamIdentifier("Order", orderId), 
            version: 0);

        // Create events to append
        var events = new[]
        {
            new AppendEvent(
                new OrderCreatedEvent(orderId, amount, customerId),
                new[] 
                {
                    new AppendMetadata("CorrelationId", Guid.NewGuid().ToString()),
                    new AppendMetadata("UserId", "user-123"),
                    new AppendMetadata("RequestId", "req-789")
                })
        };

        // Append to the event stream
        var result = await _eventStore.AppendAsync(pointer, events);
        
        return orderId;
    }

    public async Task<OrderState> GetOrderAsync(string orderId)
    {
        var pointer = new StreamPointer(
            new StreamIdentifier("Order", orderId), 
            version: 0);

        var state = new OrderState();

        // Load and replay events
        await foreach (var streamEvent in _eventStore.LoadAsync(pointer))
        {
            state = state.Apply(streamEvent.Event);
        }

        return state;
    }
}

public record OrderState
{
    public string? OrderId { get; init; }
    public decimal Amount { get; init; }
    public string? CustomerId { get; init; }
    public bool IsPaid { get; init; }
    public bool IsShipped { get; init; }

    public OrderState Apply(object @event) => @event switch
    {
        OrderCreatedEvent e => this with 
        { 
            OrderId = e.OrderId, 
            Amount = e.Amount, 
            CustomerId = e.CustomerId 
        },
        OrderPaidEvent _ => this with { IsPaid = true },
        OrderShippedEvent _ => this with { IsShipped = true },
        _ => this
    };
}
```

## 🧩 Core Concepts

### Event Attribute

Decorates event classes with metadata for serialization and routing:

```csharp
[Event("AggregateType", "EventName", SchemaVersion)]
public record MyEvent(string Data);
```

- **Aggregate**: Logical grouping (e.g., "Order", "Customer", "Invoice")
- **Name**: Event type name (e.g., "Created", "Updated", "Deleted")
- **Version**: Schema version for handling event evolution

**Wire Name Format:** The event store uses a wire name for filtering and storage: `{Aggregate}.{Name}.v{Version}`. For example, `[Event("Order", "Created", 1)]` produces the wire name `"Order.Created.v1"`. When filtering events with `eventsFilter`, you must use these wire names, not short names.

### Stream Identifier

Uniquely identifies an event stream:

```csharp
var streamId = new StreamIdentifier(
    streamType: "Order",    // Matches the Event aggregate
    streamId: "order-123"   // Unique ID for this specific order
);
```

### Stream Pointer

Points to a specific version in a stream:

```csharp
var pointer = new StreamPointer(streamIdentifier, version: 5);
```

- Version 0 = new stream
- Version > 0 = existing stream at that version
- Used for optimistic concurrency control

### Stream Event

Represents a persisted event with its metadata and global position:

```csharp
public record StreamEvent(
    StreamPointer StreamPointer,
    long GlobalPosition,              // Global ordering position across all streams
    object Event,
    IReadOnlyList<EventMetadata> Metadata
);
```

### Event Metadata

**Two metadata types for security and separation of concerns:**

#### AppendMetadata (Client-Side)
What clients provide when appending events:

```csharp
public record AppendMetadata(
    string Key,     // "CorrelationId", "UserId", etc.
    object? Value   // The metadata value
);
```

Clients use this when appending events. The system automatically tags these as `Source="Client"`.

#### EventMetadata (Storage)
What's stored and retrieved from the event store:

```csharp
public record EventMetadata(
    string Source,  // "Client", "System", "Application"
    string Key,     // The metadata key
    object? Value   // The metadata value
);
```

**Automatic Transformation:**
When you append an event with `AppendMetadata`, the system:
1. Transforms it to `EventMetadata` with `Source="Client"`
2. Adds system metadata automatically (Timestamp, StreamVersion)
3. Stores everything as `EventMetadata`

**Example:**
```csharp
// Client appends with AppendMetadata
var events = new[]
{
    new AppendEvent(
        new OrderCreatedEvent(orderId, amount),
        new[]
        {
            new AppendMetadata("CorrelationId", "abc-123"),
            new AppendMetadata("UserId", "user-456")
        })
};

// System stores as EventMetadata:
// - EventMetadata("Client", "CorrelationId", "abc-123")
// - EventMetadata("Client", "UserId", "user-456") 
// - EventMetadata("System", "Timestamp", DateTime.UtcNow)
// - EventMetadata("System", "StreamVersion", 1)
```

**Accessing Metadata Values:**

After storage, metadata values materialize as `JsonElement` due to JSON serialization. Use extension methods for safe access:

```csharp
// When reading metadata from loaded events
var timestamp = streamEvent.Metadata.GetDateTime("Timestamp");
var userId = streamEvent.Metadata.GetString("UserId");
var correlationId = streamEvent.Metadata.GetGuid("CorrelationId");

// Available: GetString, GetDateTime, GetGuid, GetInt32, GetInt64, 
//            GetDecimal, GetDouble, GetBoolean
```

**Source Types:**
- **"Client"** - Automatically assigned to all client-provided metadata
- **"System"** - Automatically added by the event store (Timestamp, StreamVersion, etc.)
- **"Application"** - Can be added by custom event handlers or middleware

**Security Benefit:** Clients cannot spoof system metadata because they only provide `AppendMetadata` (no Source field).

## 🔌 Dependency Injection Setup

### Simple Setup: All Stores Together

Use this when all stores share the same database:

```csharp
// In-memory (for testing) - must provide assemblies
services.AddEventStoreInMemory(
    "MyApp", 
    typeof(MyEvent).Assembly);

// Or use marker-type overload
services.AddEventStoreInMemory<MyEvent>("MyApp");

// SQL Server - must provide assemblies
services.AddEventStoreSqlServer(
    connectionString, 
    new[] { typeof(MyEvent).Assembly });

// Or use marker-type overload
services.AddEventStoreSqlServer<MyEvent>(connectionString);

// Custom configuration - must provide assemblies
services.AddEventStore(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(60);
    });
}, typeof(MyEvent).Assembly, typeof(MyAggregate).Assembly);

// Or use marker-type overload with custom configuration
services.AddEventStore<MyEvent>(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure();
    });
});
```

## 📝 Working with Events

### Appending Events

```csharp
public async Task AppendEventsExample(IEventStore eventStore)
{
    var streamId = new StreamIdentifier("Order", "order-123");
    var pointer = new StreamPointer(streamId, version: 0); // New stream

    var events = new[]
    {
        new AppendEvent(
            new OrderCreatedEvent("order-123", 100m, "customer-456"),
            new[]
            {
                new AppendMetadata("CorrelationId", Guid.NewGuid().ToString()),
                new AppendMetadata("CausationId", Guid.NewGuid().ToString()),
                new AppendMetadata("UserId", "user-123")
            }),
        new AppendEvent(
            new OrderPaidEvent("payment-789", 100m),
            null)
    };

    try
    {
        var result = await eventStore.AppendAsync(pointer, events);
        
        // result[0].StreamPointer.Version == 1
        // result[1].StreamPointer.Version == 2
        
        Console.WriteLine($"Appended {result.Count} events");
    }
    catch (StreamVersionConflictException ex)
    {
        // Handle concurrency conflict
        Console.WriteLine($"Version conflict: {ex.Message}");
    }
}
```

### Loading Events from a Stream

```csharp
public async Task<List<object>> LoadStreamExample(IEventStore eventStore)
{
    var streamId = new StreamIdentifier("Order", "order-123");
    var pointer = new StreamPointer(streamId, version: 0); // Load all events (version > 0)

    var events = new List<object>();

    await foreach (var streamEvent in eventStore.LoadAsync(pointer))
    {
        Console.WriteLine($"Event at version {streamEvent.StreamPointer.Version}");
        Console.WriteLine($"Event type: {streamEvent.Event.GetType().Name}");

        events.Add(streamEvent.Event);
    }

    return events;
}
```

**Note:** `LoadAsync` loads events exclusively - a pointer with version N loads events with version > N.


### Loading All Events (Event Store Pattern)

```csharp
public async Task ProcessAllEventsExample(IEventStore eventStore)
{
    long lastPosition = 0; // Start from beginning

    await foreach (var streamEvent in eventStore.LoadAllAsync(
        fromGlobalPosition: lastPosition))
    {
        Console.WriteLine($"Processing event from stream: {streamEvent.StreamPointer.Stream.StreamType}");

        // Process the event...
        await ProcessEventAsync(streamEvent.Event);

        // Track global position for resumption (NOT stream version)
        lastPosition = streamEvent.GlobalPosition;
    }
}
```

**Note:** `LoadAllAsync` loads events exclusively - if `fromGlobalPosition` is N, events with global position > N are returned. This matches `LoadAsync` behavior.

### Filtering Events

```csharp
// Filter by stream type (aggregate)
await foreach (var streamEvent in eventStore.LoadAllAsync(
    fromGlobalPosition: 0,
    streamTypeFilter: new[] { "Order", "Payment" }))
{
    // Only Order and Payment events
}

// Filter by event type using wire names: {Aggregate}.{Name}.v{Version}
await foreach (var streamEvent in eventStore.LoadAllAsync(
    fromGlobalPosition: 0,
    streamTypeFilter: new[] { "Order" },
    eventsFilter: new[] { "Order.Created.v1", "Order.Paid.v1" }))
{
    // Only OrderCreatedEvent and OrderPaidEvent instances
    // Wire names must match the [Event] attribute values
}
```

### Optimistic Concurrency

```csharp
public async Task UpdateOrderExample(IEventStore eventStore, string orderId)
{
    // Load current version
    var streamId = new StreamIdentifier("Order", orderId);
    var pointer = new StreamPointer(streamId, version: 0);
    
    long currentVersion = 0;
    await foreach (var streamEvent in eventStore.LoadAsync(pointer))
    {
        currentVersion = streamEvent.StreamPointer.Version;
    }

    // Append with expected version
    var updatePointer = new StreamPointer(streamId, currentVersion);
    var events = new[] 
    { 
        new AppendEvent(new OrderShippedEvent("TRACK-123"), null) 
    };

    try
    {
        await eventStore.AppendAsync(updatePointer, events);
    }
    catch (StreamVersionConflictException)
    {
        // Someone else modified this stream
        // Reload and retry
    }
}
```

## 💾 Snapshots

Snapshots optimize performance by storing aggregate state at specific versions, avoiding full event replay.

### Saving a Snapshot

```csharp
public async Task SaveSnapshotExample(
    IEventStore eventStore,
    ISnapshotStore snapshotStore,
    string orderId)
{
    var streamId = new StreamIdentifier("Order", orderId);
    var pointer = new StreamPointer(streamId, version: 0);

    // Rebuild state from events
    var state = new OrderState();
    long lastVersion = 0;

    await foreach (var streamEvent in eventStore.LoadAsync(pointer))
    {
        state = state.Apply(streamEvent.Event);
        lastVersion = streamEvent.StreamPointer.Version;
    }

    // Save snapshot every 100 events
    if (lastVersion % 100 == 0)
    {
        var snapshotPointer = new StreamPointer(streamId, lastVersion);
        await snapshotStore.SaveSnapshotAsync(snapshotPointer, state);
        
        Console.WriteLine($"Snapshot saved at version {lastVersion}");
    }
}
```

### Loading with Snapshot

```csharp
public async Task<OrderState> LoadOrderWithSnapshotExample(
    IEventStore eventStore,
    ISnapshotStore snapshotStore,
    string orderId)
{
    var streamId = new StreamIdentifier("Order", orderId);

    // Try to load snapshot first
    var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);

    OrderState state;
    long fromVersion;

    if (snapshot != null)
    {
        // Start from snapshot
        state = (OrderState)snapshot.State;
        fromVersion = snapshot.StreamPointer.Version;
        
        Console.WriteLine($"Loaded snapshot at version {fromVersion}");
    }
    else
    {
        // No snapshot, start from beginning
        state = new OrderState();
        fromVersion = 0;
    }

    // Replay events after snapshot
    var pointer = new StreamPointer(streamId, fromVersion);
    await foreach (var streamEvent in eventStore.LoadAsync(pointer))
    {
        state = state.Apply(streamEvent.Event);
    }

    return state;
}
```

## 📊 Projections

Projections create read models from event streams, tracked by global position for resumability.

### Creating a Projection

```csharp
public class OrderStatisticsProjection
{
    private readonly IEventStore _eventStore;
    private readonly IProjectionStore _projectionStore;
    private const string ProjectionKey = "OrderStatistics";

    public OrderStatisticsProjection(
        IEventStore eventStore,
        IProjectionStore projectionStore)
    {
        _eventStore = eventStore;
        _projectionStore = projectionStore;
    }

    public async Task<OrderStatistics> GetStatisticsAsync()
    {
        var projection = await _projectionStore.LoadProjectionAsync<OrderStatistics>(ProjectionKey);
        
        if (projection == null)
        {
            return new OrderStatistics();
        }

        return projection.State;
    }

    public async Task RebuildProjectionAsync()
    {
        // Load existing projection or start fresh
        var projection = await _projectionStore.LoadProjectionAsync<OrderStatistics>(ProjectionKey);
        
        var state = projection?.State ?? new OrderStatistics();
        var lastPosition = projection?.GlobalPosition ?? 0;

        Console.WriteLine($"Rebuilding projection from position {lastPosition}");

        // Process all events after last position
        long finalPosition = lastPosition;
        await foreach (var streamEvent in _eventStore.LoadAllAsync(
            fromGlobalPosition: lastPosition,
            streamTypeFilter: new[] { "Order" }))
        {
            // Update projection state
            state = state.Apply(streamEvent.Event);
            finalPosition = streamEvent.GlobalPosition;

            // Save periodically (every 100 events)
            if (streamEvent.GlobalPosition % 100 == 0)
            {
                await _projectionStore.SaveProjectionAsync(
                    ProjectionKey,
                    streamEvent.GlobalPosition,
                    state);
            }
        }

        // Save final state with the actual event's global position
        await _projectionStore.SaveProjectionAsync(
            ProjectionKey,
            finalPosition,
            state);

        Console.WriteLine($"Projection rebuilt. Total orders: {state.TotalOrders}");
    }
}

public record OrderStatistics
{
    public int TotalOrders { get; init; }
    public decimal TotalRevenue { get; init; }
    public int PaidOrders { get; init; }
    public long LastProcessedPosition { get; init; }

    public OrderStatistics Apply(object @event) => @event switch
    {
        OrderCreatedEvent e => this with 
        { 
            TotalOrders = TotalOrders + 1,
            TotalRevenue = TotalRevenue + e.Amount,
            LastProcessedPosition = LastProcessedPosition + 1
        },
        OrderPaidEvent _ => this with 
        { 
            PaidOrders = PaidOrders + 1,
            LastProcessedPosition = LastProcessedPosition + 1
        },
        _ => this with { LastProcessedPosition = LastProcessedPosition + 1 }
    };
}
```

### Background Projection Processing

```csharp
public class ProjectionBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProjectionBackgroundService> _logger;

    public ProjectionBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ProjectionBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var projection = scope.ServiceProvider
                    .GetRequiredService<OrderStatisticsProjection>();

                await projection.RebuildProjectionAsync();
                
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating projection");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

// Register in Program.cs
builder.Services.AddSingleton<OrderStatisticsProjection>();
builder.Services.AddHostedService<ProjectionBackgroundService>();
```

## 🔧 Advanced Scenarios

### Event Upcasting (Schema Evolution)

```csharp
[Event("Order", "Created", 2)] // Version 2
public record OrderCreatedEventV2(
    string OrderId,
    decimal Amount,
    string CustomerId,
    string Currency); // New field

public class EventUpcaster
{
    public object Upcast(object @event)
    {
        return @event switch
        {
            // Upcast V1 to V2
            OrderCreatedEvent v1 => new OrderCreatedEventV2(
                v1.OrderId,
                v1.Amount,
                v1.CustomerId,
                "USD"), // Default currency
            
            _ => @event
        };
    }
}
```

### Aggregate Root Pattern

```csharp
public class OrderAggregate
{
    private readonly List<object> _uncommittedEvents = new();
    
    public string OrderId { get; private set; }
    public OrderState State { get; private set; }
    public long Version { get; private set; }

    private OrderAggregate(string orderId)
    {
        OrderId = orderId;
        State = new OrderState();
    }

    public static OrderAggregate Create(string orderId, decimal amount, string customerId)
    {
        var aggregate = new OrderAggregate(orderId);
        aggregate.RaiseEvent(new OrderCreatedEvent(orderId, amount, customerId));
        return aggregate;
    }

    public void MarkAsPaid(string paymentId, decimal amount)
    {
        if (State.IsPaid)
            throw new InvalidOperationException("Order already paid");

        RaiseEvent(new OrderPaidEvent(paymentId, amount));
    }

    private void RaiseEvent(object @event)
    {
        State = State.Apply(@event);
        _uncommittedEvents.Add(@event);
    }

    public async Task<OrderAggregate> LoadAsync(IEventStore eventStore)
    {
        var streamId = new StreamIdentifier("Order", OrderId);
        var pointer = new StreamPointer(streamId, version: 0);

        await foreach (var streamEvent in eventStore.LoadAsync(pointer))
        {
            State = State.Apply(streamEvent.Event);
            Version = streamEvent.StreamPointer.Version;
        }

        return this;
    }

    public async Task SaveAsync(IEventStore eventStore)
    {
        if (!_uncommittedEvents.Any())
            return;

        var streamId = new StreamIdentifier("Order", OrderId);
        var pointer = new StreamPointer(streamId, Version);

        var appendEvents = _uncommittedEvents
            .Select(e => new AppendEvent(e, null))
            .ToList();

        var result = await eventStore.AppendAsync(pointer, appendEvents);
        
        Version = result.Last().StreamPointer.Version;
        _uncommittedEvents.Clear();
    }
}
```

### CQRS Pattern

```csharp
// Command Side
public class OrderCommandHandler
{
    private readonly IEventStore _eventStore;

    public async Task Handle(CreateOrderCommand command)
    {
        var aggregate = OrderAggregate.Create(
            command.OrderId,
            command.Amount,
            command.CustomerId);

        await aggregate.SaveAsync(_eventStore);
    }
}

// Query Side
public class OrderQueryHandler
{
    private readonly IProjectionStore _projectionStore;

    public async Task<OrderDto?> Handle(GetOrderQuery query)
    {
        var projection = await _projectionStore
            .LoadProjectionAsync<OrderDto>($"Order-{query.OrderId}");

        return projection?.State;
    }
}
```

## 🗄️ Database Configuration

### Included Providers

**Rickten.EventStore.EntityFramework includes:**
- **SQL Server** - First-class support with `AddEventStoreSqlServer()` helper methods
- **InMemory** - First-class support with `AddEventStoreInMemory()` helper methods for testing

### SQL Server

SQL Server is bundled with first-class helper methods:

```csharp
services.AddEventStoreSqlServer(
    Configuration.GetConnectionString("EventStore"),
    new[] { typeof(MyEvent).Assembly },
    sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(5);
        sqlOptions.CommandTimeout(60);
        sqlOptions.MigrationsAssembly("MyApp");
    });
```

### InMemory (Testing)

InMemory provider is bundled with first-class helper methods:

```csharp
services.AddEventStoreInMemory("TestDb", typeof(MyEvent).Assembly);
```

### Other EF Core Providers

**PostgreSQL, SQLite, and other EF Core providers** are supported through the generic `AddEventStore()` method, but you must install the provider package separately. Rickten does not bundle these providers or provide dedicated helper methods like `AddEventStorePostgres()`.

#### PostgreSQL

**Step 1:** Install the Npgsql provider package in your application:

```bash
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

**Step 2:** Use the generic `AddEventStore()` method:

```csharp
// Must provide assemblies
services.AddEventStore(options =>
{
    options.UseNpgsql(
        Configuration.GetConnectionString("EventStore"),
        npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(5);
        });
}, typeof(MyEvent).Assembly);

// Or use marker-type overload
services.AddEventStore<MyEvent>(options =>
{
    options.UseNpgsql(
        Configuration.GetConnectionString("EventStore"),
        npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(5);
        });
});
```

#### SQLite

**Step 1:** Install the SQLite provider package in your application:

```bash
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

**Step 2:** Use the generic `AddEventStore()` method:

```csharp
// Must provide assemblies
services.AddEventStore(options =>
{
    options.UseSqlite("Data Source=eventstore.db");
}, typeof(MyEvent).Assembly);

// Or use marker-type overload
services.AddEventStore<MyEvent>(options =>
{
    options.UseSqlite("Data Source=eventstore.db");
});
```

#### Other Providers

Any EF Core provider that supports relational databases can be used:

```csharp
// Example: MySQL (requires Pomelo.EntityFrameworkCore.MySql package)
services.AddEventStore<MyEvent>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// Example: Oracle (requires Oracle.EntityFrameworkCore package)
services.AddEventStore<MyEvent>(options =>
{
    options.UseOracle(connectionString);
});
```

### Migrations

```bash
# Add migration
dotnet ef migrations add InitialCreate --context EventStoreDbContext

# Apply migration
dotnet ef database update --context EventStoreDbContext
```

Or apply programmatically:

```csharp
using var scope = app.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();
await dbContext.Database.MigrateAsync();
```

## 🧪 Testing

### Unit Tests with In-Memory Database

```csharp
using Microsoft.Extensions.DependencyInjection;
using Rickten.EventStore;
using Xunit;

public class OrderServiceTests
{
    private IServiceProvider GetServiceProvider()
    {
        var services = new ServiceCollection();
        // Must provide assemblies containing events, aggregates, etc.
        services.AddEventStoreInMemory(
            Guid.NewGuid().ToString(),
            typeof(OrderCreatedEvent).Assembly);
        // Or use marker-type overload:
        // services.AddEventStoreInMemory<OrderCreatedEvent>(Guid.NewGuid().ToString());
        services.AddScoped<OrderService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateOrder_ShouldAppendEvent()
    {
        // Arrange
        using var serviceProvider = GetServiceProvider();
        using var scope = serviceProvider.CreateScope();
        
        var orderService = scope.ServiceProvider.GetRequiredService<OrderService>();

        // Act
        var orderId = await orderService.CreateOrderAsync(100m, "customer-123");

        // Assert
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
        var streamId = new StreamIdentifier("Order", orderId);
        var pointer = new StreamPointer(streamId, 0);

        var events = new List<object>();
        await foreach (var streamEvent in eventStore.LoadAsync(pointer))
        {
            events.Add(streamEvent.Event);
        }

        Assert.Single(events);
        Assert.IsType<OrderCreatedEvent>(events[0]);
    }

    [Fact]
    public async Task AppendAsync_ConcurrentWrites_ThrowsConflict()
    {
        // Arrange
        using var serviceProvider = GetServiceProvider();
        using var scope = serviceProvider.CreateScope();
        
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
        var streamId = new StreamIdentifier("Order", "order-123");
        var pointer = new StreamPointer(streamId, 0);

        // Act
        await eventStore.AppendAsync(pointer, new[]
        {
            new AppendEvent(new OrderCreatedEvent("order-123", 100m, "customer-1"), null)
        });

        // Assert - second append with same version should fail
        await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
        {
            await eventStore.AppendAsync(pointer, new[]
            {
                new AppendEvent(new OrderPaidEvent("payment-1", 100m), null)
            });
        });
    }
}
```

### Integration Tests

```csharp
public class OrderIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrderIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace real database with in-memory for tests
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<EventStoreDbContext>));

                if (descriptor != null)
                    services.Remove(descriptor);

                // Must provide assemblies for type registration
                services.AddEventStoreInMemory(
                    Guid.NewGuid().ToString(),
                    typeof(OrderCreatedEvent).Assembly);
                // Or use marker-type overload:
                // services.AddEventStoreInMemory<OrderCreatedEvent>(Guid.NewGuid().ToString());
            });
        });
    }

    [Fact]
    public async Task CreateOrder_ReturnsOrderId()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            Amount = 100m,
            CustomerId = "customer-123"
        });

        response.EnsureSuccessStatusCode();
        var orderId = await response.Content.ReadAsStringAsync();
        
        Assert.NotNull(orderId);
    }
}
```

## 📚 Examples

### Complete CRUD Example

See [`Examples/OrderManagement/`](Examples/OrderManagement/) for a complete working example with:
- Event sourcing
- Aggregate roots
- Command/Query separation
- Projections
- API endpoints

### Microservices Example

See [`Examples/Microservices/`](Examples/Microservices/) for a distributed system example with:
- Separate services for events, snapshots, and projections
- Message bus integration
- Event forwarding between services

## ✅ Best Practices

### 1. **Use Immutable Event Types**

```csharp
// ✅ Good - Immutable record
[Event("Order", "Created", 1)]
public record OrderCreatedEvent(string OrderId, decimal Amount);

// ❌ Bad - Mutable class
[Event("Order", "Created", 1)]
public class OrderCreatedEvent
{
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
}
```

### 2. **Include Metadata for Debugging**

```csharp
var events = new[]
{
    new AppendEvent(
        new OrderCreatedEvent(orderId, amount, customerId),
        new[]
        {
            new AppendMetadata("CorrelationId", correlationId),
            new AppendMetadata("CausationId", causationId),
            new AppendMetadata("UserId", userId),
            new AppendMetadata("RequestId", requestId)
        })
};

// System automatically adds:
// - EventMetadata("System", "Timestamp", DateTime.UtcNow)
// - EventMetadata("System", "StreamVersion", version)
```

### 3. **Snapshot Large Aggregates**

```csharp
// Snapshot every N events
if (currentVersion % 100 == 0)
{
    await snapshotStore.SaveSnapshotAsync(pointer, state);
}
```

### 4. **Handle Version Conflicts**

```csharp
const int maxRetries = 3;
for (int retry = 0; retry < maxRetries; retry++)
{
    try
    {
        await eventStore.AppendAsync(pointer, events);
        break;
    }
    catch (StreamVersionConflictException)
    {
        if (retry == maxRetries - 1)
            throw;

        // Reload and retry
        pointer = await ReloadStreamPointerAsync(streamId);
    }
}
```

### 5. **Use Separate Databases for Scale**

```csharp
// Write-optimized database for events
services.AddEventStoreOnly(options =>
    options.UseSqlServer(writeDbConnectionString));

// Read-optimized database for projections
services.AddProjectionStoreOnly(options =>
    options.UseNpgsql(readDbConnectionString));
```

### 6. **Version Your Events**

```csharp
[Event("Order", "Created", 1)]
public record OrderCreatedEventV1(string OrderId, decimal Amount);

[Event("Order", "Created", 2)]
public record OrderCreatedEventV2(
    string OrderId, 
    decimal Amount, 
    string Currency); // New in V2
```

### 7. **Keep Events Small and Focused**

```csharp
// ✅ Good - Small, focused events
[Event("Order", "Created", 1)]
public record OrderCreatedEvent(string OrderId, decimal Amount);

[Event("Order", "CustomerAssigned", 1)]
public record OrderCustomerAssignedEvent(string CustomerId);

// ❌ Bad - Large, unfocused event
[Event("Order", "Everything", 1)]
public record OrderEverythingEvent(
    string OrderId, 
    decimal Amount, 
    string CustomerId,
    List<OrderLine> Lines,
    Address ShippingAddress,
    PaymentInfo Payment);
```

## 📖 Documentation

- [API Reference](docs/API.md)
- [Architecture Guide](docs/ARCHITECTURE.md)
- [Migration Guide](docs/MIGRATION.md)
- [Performance Tuning](docs/PERFORMANCE.md)

## 🤝 Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

Built with:
- [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/)
- [.NET 10](https://dotnet.microsoft.com/)
- Inspired by [EventStore](https://www.eventstore.com/) and [Marten](https://martendb.io/)

---

**Happy Event Sourcing! 🚀**

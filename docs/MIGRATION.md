# Migration Guide

Guide for migrating to and between versions of Rickten.EventStore.

## Table of Contents

- [Migrating TO Rickten.EventStore](#migrating-to-ricktenEventStore)
  - [From EventStore](#from-eventstore)
  - [From Marten](#from-marten)
  - [From Custom Solution](#from-custom-solution)
- [Version Migrations](#version-migrations)
- [Event Schema Evolution](#event-schema-evolution)
- [Database Migrations](#database-migrations)
- [Breaking Changes](#breaking-changes)

---

## Migrating TO Rickten.EventStore

### From EventStore

If you're migrating from EventStore (the product), here's a comparison and migration path:

#### Concept Mapping

| EventStore | Rickten.EventStore |
|------------|-------------------|
| Stream | StreamIdentifier |
| StreamRevision | StreamPointer.Version |
| EventData | AppendEvent |
| ResolvedEvent | StreamEvent |
| Position | GlobalPosition |

#### Code Comparison

**EventStore (old):**
```csharp
var streamName = $"order-{orderId}";
var eventData = new EventData(
    Uuid.NewUuid(),
    "OrderCreated",
    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(orderCreated)));

await eventStore.AppendToStreamAsync(
    streamName,
    StreamState.NoStream,
    new[] { eventData });
```

**Rickten.EventStore (new):**
```csharp
var streamId = new StreamIdentifier("Order", orderId);
var pointer = new StreamPointer(streamId, version: 0);

await eventStore.AppendAsync(pointer, new[]
{
    new AppendEvent(orderCreated, new[]
    {
        new AppendMetadata("CorrelationId", correlationId)
    })
});
```

#### Migration Steps

1. **Export Events** from EventStore:
```csharp
public async Task ExportFromEventStore()
{
    var connection = EventStoreConnection.Create(...);
    await connection.ConnectAsync();
    
    var allEvents = connection.ReadAllEventsForwardAsync(
        Position.Start,
        4096,
        false);
    
    await foreach (var resolvedEvent in allEvents)
    {
        // Export to JSON or CSV
        await File.AppendAllTextAsync("export.json",
            JsonSerializer.Serialize(new
            {
                StreamId = resolvedEvent.Event.EventStreamId,
                EventType = resolvedEvent.Event.EventType,
                Data = Encoding.UTF8.GetString(resolvedEvent.Event.Data.ToArray()),
                Metadata = Encoding.UTF8.GetString(resolvedEvent.Event.Metadata.ToArray())
            }));
    }
}
```

2. **Import to Rickten.EventStore**:
```csharp
public async Task ImportToRicktenEventStore(IEventStore eventStore)
{
    var lines = await File.ReadAllLinesAsync("export.json");
    
    foreach (var line in lines)
    {
        var exported = JsonSerializer.Deserialize<ExportedEvent>(line);
        
        // Parse stream type and identifier from StreamId
        var parts = exported.StreamId.Split('-');
        var streamType = parts[0]; // e.g., "order"
        var identifier = parts[1];  // e.g., "123"
        
        var streamId = new StreamIdentifier(streamType, identifier);
        
        // Deserialize event
        var eventType = Type.GetType(exported.EventType);
        var @event = JsonSerializer.Deserialize(exported.Data, eventType);
        
        // Append (will need to track versions during import)
        var pointer = new StreamPointer(streamId, currentVersion);
        await eventStore.AppendAsync(pointer, new[]
        {
            new AppendEvent(@event, null)
        });
    }
}
```

3. **Update Application Code**:
   - Replace EventStore client with Rickten.EventStore
   - Update event append code
   - Update event reading code
   - Test thoroughly

---

### From Marten

Marten uses PostgreSQL for event sourcing. Here's how to migrate:

#### Concept Mapping

| Marten | Rickten.EventStore |
|--------|-------------------|
| Stream Name | StreamIdentifier |
| Version | StreamPointer.Version |
| Event | AppendEvent |
| IEvent | StreamEvent |
| Sequence | GlobalPosition |

#### Migration Steps

1. **Export from Marten**:
```csharp
public async Task ExportFromMarten(IDocumentStore store)
{
    using var session = store.QuerySession();
    
    var events = await session.Events.QueryAllRawEvents()
        .OrderBy(e => e.Sequence)
        .ToListAsync();
    
    foreach (var @event in events)
    {
        await File.AppendAllTextAsync("marten-export.json",
            JsonSerializer.Serialize(new
            {
                StreamId = @event.StreamId,
                StreamKey = @event.StreamKey,
                Version = @event.Version,
                Sequence = @event.Sequence,
                EventType = @event.EventType.FullName,
                Data = JsonSerializer.Serialize(@event.Data)
            }));
    }
}
```

2. **Import to Rickten.EventStore**: Similar process as EventStore migration

---

### From Custom Solution

If you have a custom event sourcing implementation:

1. **Analyze Current Schema**:
```sql
-- Example: What does your current schema look like?
SELECT * FROM EventTable;
```

2. **Map to Rickten.EventStore Types**:
   - Identify stream identifiers
   - Map versions/sequences
   - Extract event data
   - Map metadata

3. **Write Migration Script**:
```csharp
public async Task MigrateCustomEvents()
{
    using var connection = new SqlConnection(oldConnectionString);
    var events = await connection.QueryAsync<CustomEvent>(
        "SELECT * FROM EventTable ORDER BY Sequence");
    
    foreach (var customEvent in events)
    {
        var streamId = new StreamIdentifier(
            customEvent.AggregateType,
            customEvent.AggregateId);
        
        var pointer = new StreamPointer(streamId, customEvent.Version);
        
        // Deserialize your custom event format
        var @event = DeserializeCustomEvent(customEvent);
        
        await eventStore.AppendAsync(pointer, new[]
        {
            new AppendEvent(@event, null)
        });
    }
}
```

---

## Version Migrations

### Version 1.0.0 → 1.1.0 (Hypothetical)

Example of future version migration:

**Breaking Changes:**
- None (this is a minor version)

**New Features:**
- New projection features
- Performance improvements

**Migration Steps:**
1. Update NuGet package: `dotnet add package Rickten.EventStore.EntityFramework --version 1.1.0`
2. Run database migrations (if any)
3. Test application

---

## Event Schema Evolution

### Version Your Events

Always version your events from day one:

```csharp
[Event("Order", "Created", 1)] // Version 1
public record OrderCreatedEventV1(
    string OrderId,
    decimal Amount,
    string CustomerId);

[Event("Order", "Created", 2)] // Version 2 - Added Currency
public record OrderCreatedEventV2(
    string OrderId,
    decimal Amount,
    string CustomerId,
    string Currency);
```

### Upcasting Strategy

Convert old events to new schema on read:

```csharp
public class EventUpcaster
{
    public object Upcast(object @event) => @event switch
    {
        // Upcast V1 to V2
        OrderCreatedEventV1 v1 => new OrderCreatedEventV2(
            v1.OrderId,
            v1.Amount,
            v1.CustomerId,
            "USD"), // Default currency for old events
        
        // Upcast V2 to V3 (future)
        OrderCreatedEventV2 v2 => new OrderCreatedEventV3(
            v2.OrderId,
            v2.Amount,
            v2.CustomerId,
            v2.Currency,
            DateTime.UtcNow), // Add timestamp
        
        _ => @event // Already latest version
    };
}
```

### Use in Application

```csharp
public async Task<OrderState> LoadOrderAsync(string orderId)
{
    var streamId = new StreamIdentifier("Order", orderId);
    var pointer = new StreamPointer(streamId, 0);
    
    var state = new OrderState();
    var upcaster = new EventUpcaster();
    
    await foreach (var streamEvent in eventStore.LoadAsync(pointer))
    {
        // Upcast before applying
        var upcastedEvent = upcaster.Upcast(streamEvent.Event);
        state = state.Apply(upcastedEvent);
    }
    
    return state;
}
```

### Migration Options

#### Option 1: Lazy Migration (Recommended)

Upcast events on read, never modify stored events.

**Pros:**
- ✅ Safe (no data modification)
- ✅ Fast (no rewriting)
- ✅ Reversible

**Cons:**
- ❌ Slight read overhead
- ❌ Old events remain in database

#### Option 2: In-Place Migration

Rewrite events in database with new schema.

```csharp
public async Task MigrateEventsInPlace()
{
    var connection = new SqlConnection(connectionString);
    var events = await connection.QueryAsync<EventEntity>(
        "SELECT * FROM Events WHERE EventType LIKE '%OrderCreatedEventV1%'");
    
    foreach (var eventEntity in events)
    {
        var v1 = JsonSerializer.Deserialize<OrderCreatedEventV1>(eventEntity.EventData);
        var v2 = new OrderCreatedEventV2(v1.OrderId, v1.Amount, v1.CustomerId, "USD");
        
        await connection.ExecuteAsync(
            "UPDATE Events SET EventData = @Data, EventType = @Type WHERE Id = @Id",
            new
            {
                Data = JsonSerializer.Serialize(v2),
                Type = typeof(OrderCreatedEventV2).AssemblyQualifiedName,
                eventEntity.Id
            });
    }
}
```

**Pros:**
- ✅ Cleaner database
- ✅ No upcast overhead

**Cons:**
- ❌ Risky (modifying source of truth)
- ❌ Slow for large datasets
- ❌ Irreversible (backup required!)

#### Option 3: Copy-Transform-Replace

Create new stream with transformed events.

```csharp
public async Task CopyTransformReplace(string oldStreamId, string newStreamId)
{
    // 1. Copy old stream to new stream with upcasting
    var oldPointer = new StreamPointer(
        new StreamIdentifier("Order", oldStreamId), 0);
    
    var newPointer = new StreamPointer(
        new StreamIdentifier("Order", newStreamId), 0);
    
    var events = new List<AppendEvent>();
    
    await foreach (var e in eventStore.LoadAsync(oldPointer))
    {
        var upcast = upcaster.Upcast(e.Event);
        events.Add(new AppendEvent(upcast, null));
    }
    
    await eventStore.AppendAsync(newPointer, events);
    
    // 2. Update application to use new stream
    // 3. Optionally: Mark old stream as archived
}
```

---

## Database Migrations

### Entity Framework Migrations

#### Creating a Migration

```bash
dotnet ef migrations add InitialCreate --context EventStoreDbContext
```

#### Applying Migrations

**Development:**
```bash
dotnet ef database update --context EventStoreDbContext
```

**Production (programmatically):**
```csharp
public static async Task Main(string[] args)
{
    var host = CreateHostBuilder(args).Build();
    
    // Apply migrations on startup
    using (var scope = host.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider
            .GetRequiredService<EventStoreDbContext>();
        
        await dbContext.Database.MigrateAsync();
    }
    
    await host.RunAsync();
}
```

#### Custom Migration for Data Transform

```csharp
public partial class TransformOrderEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Custom SQL to transform data
        migrationBuilder.Sql(@"
            UPDATE Events
            SET EventData = JSON_MODIFY(EventData, '$.currency', 'USD')
            WHERE EventType LIKE '%OrderCreatedEventV1%'
              AND JSON_VALUE(EventData, '$.currency') IS NULL
        ");
    }
    
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Rollback transformation
        migrationBuilder.Sql(@"
            UPDATE Events
            SET EventData = JSON_MODIFY(EventData, '$.currency', NULL)
            WHERE EventType LIKE '%OrderCreatedEventV1%'
        ");
    }
}
```

---

## Breaking Changes

### Version 1.0.0

Initial release - no breaking changes.

### Future Versions

Breaking changes will be documented here with migration paths.

---

## Backup Strategy

**CRITICAL:** Always backup before migrations!

### Backup Events

```sql
-- SQL Server
BACKUP DATABASE [EventStore] 
TO DISK = 'C:\Backups\EventStore_BeforeMigration.bak'
WITH FORMAT, COMPRESSION;

-- Or export to JSON
SELECT 
    StreamType,
    StreamIdentifier,
    Version,
    EventType,
    EventData,
    Metadata,
    CreatedAt
FROM Events
ORDER BY GlobalPosition
FOR JSON PATH;
```

### Restore Events

```sql
-- SQL Server
RESTORE DATABASE [EventStore]
FROM DISK = 'C:\Backups\EventStore_BeforeMigration.bak'
WITH REPLACE;
```

---

## Testing Migrations

### Migration Test Checklist

- [ ] Backup production data
- [ ] Run migration in test environment
- [ ] Verify event count matches
- [ ] Verify stream versions are correct
- [ ] Test reading events
- [ ] Test appending new events
- [ ] Test projections rebuild correctly
- [ ] Load test with production-like load
- [ ] Have rollback plan ready

### Test Migration Script

```csharp
[Fact]
public async Task MigrationTest_AllEventsPreserved()
{
    // Arrange: Get event count before
    var beforeCount = await CountEventsAsync();
    
    // Act: Run migration
    await RunMigrationAsync();
    
    // Assert: Verify count after
    var afterCount = await CountEventsAsync();
    Assert.Equal(beforeCount, afterCount);
}

[Fact]
public async Task MigrationTest_CanReadEventsAfterMigration()
{
    // Arrange
    var orderId = "test-order-123";
    await CreateTestOrderAsync(orderId);
    
    // Act: Run migration
    await RunMigrationAsync();
    
    // Assert: Can still read
    var state = await LoadOrderAsync(orderId);
    Assert.NotNull(state);
}
```

---

## Support

Need help with migration?

- Open an [issue](https://github.com/rickten/rickten.eventstore/issues)
- Check [discussions](https://github.com/rickten/rickten.eventstore/discussions)
- Review [architecture docs](ARCHITECTURE.md)

---

## Best Practices

✅ **Do:**
- Version events from day one
- Use upcasting for schema changes
- Test migrations thoroughly
- Keep backups
- Document breaking changes
- Provide migration scripts

❌ **Don't:**
- Modify events in-place without backups
- Skip version numbers
- Change event names (breaks deserialization)
- Delete old event versions (breaks history)
- Rush production migrations

---

For more information:
- [Architecture Guide](ARCHITECTURE.md)
- [API Reference](API.md)
- [Performance Guide](PERFORMANCE.md)

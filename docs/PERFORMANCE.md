# Performance Tuning Guide

Optimize Rickten.EventStore for your specific workload and requirements.

## Table of Contents

- [Performance Overview](#performance-overview)
- [Write Performance](#write-performance)
- [Read Performance](#read-performance)
- [Snapshot Strategy](#snapshot-strategy)
- [Projection Performance](#projection-performance)
- [Database Optimization](#database-optimization)
- [Monitoring](#monitoring)
- [Benchmarks](#benchmarks)

---

## Performance Overview

### Key Metrics

- **Write Throughput**: Events/second appended
- **Read Latency**: Time to load aggregate
- **Projection Lag**: Delay in projection updates
- **Storage Growth**: Database size over time

### Performance Characteristics

| Operation | Complexity | Notes |
|-----------|-----------|-------|
| Append Single Event | O(1) | Single DB insert |
| Append Batch | O(n) | n inserts in transaction |
| Load Stream | O(n) | n = events in stream |
| Load Stream with Snapshot | O(k) | k = events since snapshot |
| Load All Events | O(m) | m = total events |
| Load All with Filter | O(m) | Depends on index usage |

---

## Write Performance

### Batch Appends

Append multiple events in one call to reduce round trips:

```csharp
// ❌ Bad: Multiple round trips
foreach (var @event in events)
{
    await eventStore.AppendAsync(pointer, new[] { 
        new AppendEvent(@event, null) 
    });
    pointer = new StreamPointer(pointer.Stream, pointer.Version + 1);
}

// ✅ Good: Single round trip
await eventStore.AppendAsync(pointer, events.Select(e => 
    new AppendEvent(e, null)).ToList());
```

**Performance Improvement:** 10-100x faster depending on batch size

### Optimize Metadata

Only include essential metadata:

```csharp
// ❌ Bad: Excessive metadata
new AppendEvent(@event, new[]
{
    new AppendMetadata("CorrelationId", correlationId),
    new AppendMetadata("CausationId", causationId),
    new AppendMetadata("UserId", userId),
    new AppendMetadata("UserName", userName),
    new AppendMetadata("UserEmail", userEmail),
    new AppendMetadata("IPAddress", ipAddress),
    new AppendMetadata("UserAgent", userAgent),
    // ... 20 more metadata fields
});

// ✅ Good: Essential metadata only
new AppendEvent(@event, new[]
{
    new AppendMetadata("CorrelationId", correlationId),
    new AppendMetadata("UserId", userId)
});
```

**Why:** Less data to serialize, smaller payloads, faster inserts

### Connection Pooling

Configure EF Core connection pooling:

```csharp
services.AddEventStore(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.MaxBatchSize(100);  // Batch multiple operations
        sqlOptions.CommandTimeout(30); // Timeout in seconds
    });
});

// Configure DbContext pooling
services.AddDbContextPool<EventStoreDbContext>(options =>
{
    options.UseSqlServer(connectionString);
}, poolSize: 128); // Default is 128
```

### Async All The Way

Never block on async operations:

```csharp
// ❌ Bad: Blocking
var result = eventStore.AppendAsync(pointer, events).Result;

// ✅ Good: Async
var result = await eventStore.AppendAsync(pointer, events);
```

---

## Read Performance

### Use Snapshots

Dramatically reduce read times for large aggregates:

```csharp
public async Task<OrderState> LoadOrderAsync(string orderId)
{
    var streamId = new StreamIdentifier("Order", orderId);
    
    // Try to load snapshot first
    var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);
    
    OrderState state;
    long fromVersion;
    
    if (snapshot != null)
    {
        state = (OrderState)snapshot.State;
        fromVersion = snapshot.StreamPointer.Version;
    }
    else
    {
        state = new OrderState();
        fromVersion = 0;
    }
    
    // Only load events after snapshot
    var pointer = new StreamPointer(streamId, fromVersion);
    await foreach (var e in eventStore.LoadAsync(pointer))
    {
        state = state.Apply(e.Event);
    }
    
    return state;
}
```

**Performance:** Without snapshot: O(n), With snapshot: O(k) where k << n

### Snapshot Frequency

Balance storage vs performance:

```csharp
// Snapshot every N events
public async Task SaveIfNeeded(StreamPointer pointer, OrderState state)
{
    const int snapshotInterval = 50; // Tune this
    
    if (pointer.Version % snapshotInterval == 0)
    {
        await snapshotStore.SaveSnapshotAsync(pointer, state);
    }
}
```

**Recommendations:**
- **High Write Volume**: Every 50-100 events
- **Low Write Volume**: Every 10-20 events
- **Very Large Aggregates**: Every 20-30 events

### Stream Loading Optimization

Use `IAsyncEnumerable` to avoid loading all events into memory:

```csharp
// ✅ Good: Streaming (low memory)
await foreach (var e in eventStore.LoadAsync(pointer))
{
    state = state.Apply(e.Event);
}

// ❌ Bad: Loading all at once (high memory)
var allEvents = await eventStore.LoadAsync(pointer).ToListAsync();
foreach (var e in allEvents)
{
    state = state.Apply(e.Event);
}
```

### Caching

Cache frequently accessed aggregates:

```csharp
public class CachedEventStore
{
    private readonly IEventStore _inner;
    private readonly IMemoryCache _cache;
    
    public async Task<OrderState> LoadOrderAsync(string orderId)
    {
        var cacheKey = $"Order:{orderId}";
        
        if (_cache.TryGetValue(cacheKey, out OrderState? cached))
        {
            return cached!;
        }
        
        var state = await LoadFromEventsAsync(orderId);
        
        _cache.Set(cacheKey, state, TimeSpan.FromMinutes(5));
        
        return state;
    }
}
```

**Warning:** Invalidation is complex - use carefully!

---

## Snapshot Strategy

### When to Snapshot

```csharp
public class SnapshotPolicy
{
    public bool ShouldSnapshot(long version, int eventsSinceSnapshot)
    {
        // Strategy 1: Fixed interval
        if (version % 100 == 0)
            return true;
        
        // Strategy 2: Time-based
        if (TimeSinceLastSnapshot > TimeSpan.FromHours(1))
            return true;
        
        // Strategy 3: Event count since last snapshot
        if (eventsSinceSnapshot >= 50)
            return true;
        
        return false;
    }
}
```

### Snapshot Size Considerations

Keep snapshots small:

```csharp
// ❌ Bad: Snapshotting entire aggregate with collections
public record OrderState
{
    public List<OrderLine> Lines { get; init; } = new(); // Can be huge!
    public List<AuditLog> AuditLog { get; init; } = new(); // Growing forever!
}

// ✅ Good: Snapshot essential state only
public record OrderState
{
    public string Status { get; init; }
    public decimal Total { get; init; }
    public int LineCount { get; init; } // Count, not full list
}

// ✅ Better: Separate read model
public record OrderSummary // Snapshot this
{
    public string Status { get; init; }
    public decimal Total { get; init; }
}

public record OrderDetails // Rebuild from events
{
    public List<OrderLine> Lines { get; init; }
}
```

---

## Projection Performance

### Incremental Updates

Always track global position:

```csharp
public async Task UpdateProjectionAsync()
{
    var projection = await projectionStore
        .LoadProjectionAsync<OrderStats>("OrderStats");
    
    var state = projection?.State ?? new OrderStats();
    var lastPosition = projection?.GlobalPosition ?? 0;
    
    // Only process new events
    await foreach (var e in eventStore.LoadAllAsync(
        fromGlobalPosition: lastPosition,
        streamTypeFilter: new[] { "Order" }))
    {
        state = state.Apply(e.Event);
        
        // Save periodically (every 100 events)
        if (e.StreamPointer.Version % 100 == 0)
        {
            await projectionStore.SaveProjectionAsync(
                "OrderStats", 
                e.StreamPointer.Version, 
                state);
        }
    }
    
    // Save final state
    await projectionStore.SaveProjectionAsync(
        "OrderStats", 
        state.LastPosition, 
        state);
}
```

### Batch Projection Updates

Process events in batches:

```csharp
public async Task UpdateProjectionBatchedAsync()
{
    const int batchSize = 1000;
    var events = new List<StreamEvent>();
    
    await foreach (var e in eventStore.LoadAllAsync(lastPosition))
    {
        events.Add(e);
        
        if (events.Count >= batchSize)
        {
            await ProcessBatchAsync(events);
            events.Clear();
        }
    }
    
    // Process remaining
    if (events.Any())
    {
        await ProcessBatchAsync(events);
    }
}
```

### Parallel Projections

Run multiple projections concurrently:

```csharp
public async Task UpdateAllProjectionsAsync()
{
    var tasks = new[]
    {
        UpdateOrderStatsAsync(),
        UpdateCustomerStatsAsync(),
        UpdateProductStatsAsync()
    };
    
    await Task.WhenAll(tasks);
}
```

### Dedicated Projection Database

Use separate database for read-optimized storage:

```csharp
// Events in SQL Server (write-optimized)
services.AddEventStoreOnly(options =>
    options.UseSqlServer(eventsConnectionString));

// Projections in PostgreSQL (read-optimized)
services.AddProjectionStoreOnly(options =>
    options.UseNpgsql(projectionsConnectionString));
```

---

## Database Optimization

### Indexes

Ensure proper indexes exist:

```sql
-- Stream queries
CREATE INDEX IX_Events_Stream 
ON Events (StreamType, StreamIdentifier);

-- Global position queries
CREATE INDEX IX_Events_GlobalPosition 
ON Events (GlobalPosition);

-- Filtered queries
CREATE INDEX IX_Events_StreamType_GlobalPosition 
ON Events (StreamType, GlobalPosition);

-- Include event type for filtering
CREATE INDEX IX_Events_EventType_GlobalPosition 
ON Events (EventType, GlobalPosition);
```

### Partitioning

Partition large tables by date:

```sql
-- SQL Server example
CREATE PARTITION FUNCTION EventDatePartition (DATETIME2)
AS RANGE RIGHT FOR VALUES 
    ('2025-01-01', '2025-02-01', '2025-03-01', ...);

CREATE PARTITION SCHEME EventDateScheme
AS PARTITION EventDatePartition
ALL TO ([PRIMARY]);

-- Apply to Events table
CREATE TABLE Events (
    ...
    CreatedAt DATETIME2 NOT NULL,
    ...
) ON EventDateScheme(CreatedAt);
```

### Query Optimization

Monitor and optimize slow queries:

```csharp
// Enable query logging
services.AddDbContext<EventStoreDbContext>(options =>
{
    options.UseSqlServer(connectionString)
           .EnableSensitiveDataLogging() // Dev only!
           .LogTo(Console.WriteLine, LogLevel.Information);
});
```

### Statistics and Maintenance

Keep database statistics updated:

```sql
-- SQL Server
UPDATE STATISTICS Events WITH FULLSCAN;

-- PostgreSQL
ANALYZE Events;
```

---

## Monitoring

### Key Metrics to Track

```csharp
public class EventStoreMetrics
{
    // Write metrics
    public long EventsAppendedCount { get; set; }
    public TimeSpan AverageAppendLatency { get; set; }
    public int AppendErrors { get; set; }
    
    // Read metrics
    public long StreamsLoadedCount { get; set; }
    public TimeSpan AverageLoadLatency { get; set; }
    
    // Storage metrics
    public long TotalEvents { get; set; }
    public long DatabaseSizeBytes { get; set; }
    
    // Projection metrics
    public long ProjectionLag { get; set; } // Events behind
    public TimeSpan ProjectionUpdateFrequency { get; set; }
}
```

### Application Insights

Integrate with monitoring:

```csharp
public class InstrumentedEventStore : IEventStore
{
    private readonly IEventStore _inner;
    private readonly TelemetryClient _telemetry;
    
    public async Task<IReadOnlyList<StreamEvent>> AppendAsync(...)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.AppendAsync(...);
            
            _telemetry.TrackMetric("EventStore.Append.Duration", sw.ElapsedMilliseconds);
            _telemetry.TrackMetric("EventStore.Append.EventCount", result.Count);
            
            return result;
        }
        catch (Exception ex)
        {
            _telemetry.TrackException(ex);
            throw;
        }
    }
}
```

---

## Benchmarks

### Typical Performance

Based on SQL Server on moderate hardware:

| Operation | Events | Latency | Throughput |
|-----------|--------|---------|------------|
| Append Single | 1 | 5-10ms | 100-200 ops/sec |
| Append Batch (10) | 10 | 15-20ms | 500-700 events/sec |
| Append Batch (100) | 100 | 50-80ms | 1,250-2,000 events/sec |
| Load Stream (100 events) | 100 | 20-30ms | - |
| Load Stream (1,000 events) | 1,000 | 100-150ms | - |
| Load with Snapshot (50 new) | 50 | 15-20ms | - |
| Load All (10,000 events) | 10,000 | 500-800ms | - |

### Benchmark Code

```csharp
[MemoryDiagnoser]
public class EventStoreBenchmarks
{
    private IEventStore _eventStore;
    
    [Benchmark]
    public async Task AppendSingleEvent()
    {
        var pointer = new StreamPointer(
            new StreamIdentifier("Order", Guid.NewGuid().ToString()), 0);
        
        await _eventStore.AppendAsync(pointer, new[]
        {
            new AppendEvent(new OrderCreatedEvent("1", 100m), null)
        });
    }
    
    [Benchmark]
    public async Task AppendBatch10()
    {
        var pointer = new StreamPointer(
            new StreamIdentifier("Order", Guid.NewGuid().ToString()), 0);
        
        var events = Enumerable.Range(0, 10)
            .Select(i => new AppendEvent(new OrderCreatedEvent($"{i}", 100m), null))
            .ToList();
        
        await _eventStore.AppendAsync(pointer, events);
    }
}
```

---

## Best Practices Summary

### Do's ✅

- **Use snapshots** for aggregates with >50 events
- **Batch appends** when possible
- **Stream events** instead of loading all
- **Index appropriately** for your query patterns
- **Monitor performance** metrics
- **Use async/await** consistently
- **Separate databases** for high scale
- **Cache carefully** with proper invalidation

### Don'ts ❌

- **Don't snapshot too frequently** (storage waste)
- **Don't load entire stream** if you only need recent events
- **Don't block** on async operations
- **Don't over-index** (write performance impact)
- **Don't serialize large objects** in snapshots
- **Don't ignore concurrency conflicts** (retry logic)
- **Don't forget connection pooling** configuration

---

## Troubleshooting

### Slow Writes

**Check:**
1. Index overhead (too many indexes?)
2. Transaction size (batch too large?)
3. Network latency (database location?)
4. Lock contention (high concurrency?)

**Solutions:**
- Reduce indexes if not needed
- Optimize batch sizes (test different values)
- Use closer database or faster network
- Partition by stream type

### Slow Reads

**Check:**
1. Missing indexes
2. No snapshots
3. Loading all events
4. Serialization overhead

**Solutions:**
- Add appropriate indexes
- Implement snapshot strategy
- Use streaming with `IAsyncEnumerable`
- Profile serialization (consider MessagePack, Protobuf)

### Projection Lag

**Check:**
1. Projection complexity
2. Update frequency
3. Database write speed
4. Event volume

**Solutions:**
- Simplify projection logic
- Batch updates
- Use dedicated read database
- Scale out projection workers

---

For more information:
- [Architecture Guide](ARCHITECTURE.md)
- [API Reference](API.md)
- [Migration Guide](MIGRATION.md)

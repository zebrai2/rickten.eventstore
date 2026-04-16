using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using Rickten.EventStore.TypeMetadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rickten.EventStore.Tests.Integration;

/// <summary>
/// Abstract base class for integration tests across all database providers.
/// Each provider (SQL Server, PostgreSQL, SQLite) inherits from this class
/// and provides provider-specific setup while sharing all test logic.
/// </summary>
public abstract class EventStoreIntegrationTestsBase
{
    private static readonly ITypeMetadataRegistry Registry = TestTypeMetadataRegistry.Create();

    /// <summary>
    /// Gets the aggregate type name to use for test events.
    /// Each provider must use a unique aggregate name to avoid event type conflicts.
    /// </summary>
    protected abstract string AggregateType { get; }

    /// <summary>
    /// Checks if the provider is available and skips the test if not.
    /// </summary>
    protected abstract void SkipIfNotAvailable();

    /// <summary>
    /// Creates a new EventStoreDbContext for the provider.
    /// </summary>
    protected abstract EventStoreDbContext CreateContext();

    /// <summary>
    /// Creates a test event representing a product creation.
    /// </summary>
    protected abstract object CreateProductCreatedEvent(string name, decimal price);

    /// <summary>
    /// Creates a test event representing a product update.
    /// </summary>
    protected abstract object CreateProductUpdatedEvent(decimal newPrice);

    /// <summary>
    /// Validates that a loaded event is a product created event with expected values.
    /// </summary>
    protected abstract void AssertProductCreatedEvent(object evt, string expectedName, decimal expectedPrice);

    private EntityFramework.EventStore CreateEventStore() => new EntityFramework.EventStore(CreateContext(), Registry);
    private EntityFramework.SnapshotStore CreateSnapshotStore() => new EntityFramework.SnapshotStore(CreateContext(), Registry);

    [SkippableFact]
    public async Task ValueGeneratedOnAdd_AssignsSequentialIds()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();
        var pointer = new StreamPointer(new StreamIdentifier(AggregateType, "seq-1"), 0);

        var events = new List<AppendEvent>
        {
            new AppendEvent(CreateProductCreatedEvent("Widget", 10m), null),
            new AppendEvent(CreateProductUpdatedEvent(12m), null),
            new AppendEvent(CreateProductUpdatedEvent(15m), null)
        };

        var result = await store.AppendAsync(pointer, events);

        Assert.Equal(3, result.Count);

        // All providers should assign sequential IDs
        Assert.True(result[0].GlobalPosition > 0);
        Assert.Equal(result[0].GlobalPosition + 1, result[1].GlobalPosition);
        Assert.Equal(result[1].GlobalPosition + 1, result[2].GlobalPosition);
    }

    [SkippableFact]
    public async Task UniqueConstraint_PreventsDuplicateVersions()
    {
        SkipIfNotAvailable();

        var streamId = new StreamIdentifier(AggregateType, "concurrent");
        var pointer = new StreamPointer(streamId, 0);

        var store1 = CreateEventStore();
        await store1.AppendAsync(pointer, new List<AppendEvent>
        {
            new AppendEvent(CreateProductCreatedEvent("First", 100m), null)
        });

        var store2 = CreateEventStore();
        await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
        {
            await store2.AppendAsync(pointer, new List<AppendEvent>
            {
                new AppendEvent(CreateProductCreatedEvent("Second", 200m), null)
            });
        });
    }

    [SkippableFact]
    public async Task ConcurrentAppends_DatabaseConstraintEnforcement()
    {
        SkipIfNotAvailable();

        var streamId = new StreamIdentifier(AggregateType, "race");
        var pointer = new StreamPointer(streamId, 0);

        // Create multiple stores to simulate concurrent operations
        var store1 = CreateEventStore();
        var store2 = CreateEventStore();
        var store3 = CreateEventStore();

        // All try to append at version 0 simultaneously
        var tasks = new[]
        {
            Task.Run(() => store1.AppendAsync(pointer, new List<AppendEvent>
            {
                new AppendEvent(CreateProductCreatedEvent("Store1", 100m), null)
            })),
            Task.Run(() => store2.AppendAsync(pointer, new List<AppendEvent>
            {
                new AppendEvent(CreateProductCreatedEvent("Store2", 200m), null)
            })),
            Task.Run(() => store3.AppendAsync(pointer, new List<AppendEvent>
            {
                new AppendEvent(CreateProductCreatedEvent("Store3", 300m), null)
            }))
        };

        var results = await Task.WhenAll(tasks.Select(t => 
            t.ContinueWith(x => new { Success = x.IsCompletedSuccessfully, Exception = x.Exception })));

        // Exactly one should succeed
        var successCount = results.Count(r => r.Success);
        Assert.Equal(1, successCount);

        // Verify only one event in database
        var readStore = CreateEventStore();
        var events = new List<StreamEvent>();
        await foreach (var evt in readStore.LoadAsync(new StreamPointer(streamId, 0)))
        {
            events.Add(evt);
        }
        Assert.Single(events);
    }

    [SkippableFact]
    public async Task TransactionIsolation_PreservesConsistency()
    {
        SkipIfNotAvailable();

        // Verify that failed appends don't leave partial data
        var streamId = new StreamIdentifier(AggregateType, "transaction");
        var store = CreateEventStore();

        // First successful append
        await store.AppendAsync(new StreamPointer(streamId, 0), new List<AppendEvent>
        {
            new AppendEvent(CreateProductCreatedEvent("Initial", 50m), null)
        });

        // Attempt to append at wrong version (should fail atomically)
        try
        {
            await store.AppendAsync(new StreamPointer(streamId, 0), new List<AppendEvent>
            {
                new AppendEvent(CreateProductUpdatedEvent(75m), null),
                new AppendEvent(CreateProductUpdatedEvent(100m), null)
            });
            Assert.Fail("Should have thrown StreamVersionConflictException");
        }
        catch (StreamVersionConflictException)
        {
            // Expected
        }

        // Verify database is in consistent state - should only have 1 event
        var events = new List<StreamEvent>();
        await foreach (var evt in store.LoadAsync(new StreamPointer(streamId, 0)))
        {
            events.Add(evt);
        }
        Assert.Single(events);
        Assert.Equal(1, events[0].StreamPointer.Version);
    }

    [SkippableFact]
    public async Task IndexPerformance_GlobalPositionQueries()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();

        // Create multiple streams with events
        for (int i = 1; i <= 10; i++)
        {
            var streamId = new StreamIdentifier(AggregateType, $"perf-{i}");
            await store.AppendAsync(new StreamPointer(streamId, 0), new List<AppendEvent>
            {
                new AppendEvent(CreateProductCreatedEvent($"Product{i}", i * 10m), null),
                new AppendEvent(CreateProductUpdatedEvent(i * 15m), null)
            });
        }

        // Query by global position should use index
        var eventsFromPosition5 = new List<StreamEvent>();
        await foreach (var evt in store.LoadAllAsync(5))
        {
            eventsFromPosition5.Add(evt);
        }

        Assert.True(eventsFromPosition5.Count > 0);
        Assert.All(eventsFromPosition5, evt => Assert.True(evt.GlobalPosition >= 5));
    }

    [SkippableFact]
    public async Task StreamTypeFiltering_UsesIndex()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();

        // Create events for different stream types
        await store.AppendAsync(new StreamPointer(new StreamIdentifier(AggregateType, "1"), 0),
            new List<AppendEvent> { new AppendEvent(CreateProductCreatedEvent("P1", 100m), null) });

        await store.AppendAsync(new StreamPointer(new StreamIdentifier(AggregateType, "2"), 0),
            new List<AppendEvent> { new AppendEvent(CreateProductCreatedEvent("P2", 200m), null) });

        // Filter should use IX_Events_Stream index
        var productEvents = new List<StreamEvent>();
        await foreach (var evt in store.LoadAllAsync(0, new[] { AggregateType }))
        {
            productEvents.Add(evt);
        }

        Assert.Equal(2, productEvents.Count);
        Assert.All(productEvents, evt => Assert.Equal(AggregateType, evt.StreamPointer.Stream.StreamType));
    }

    [SkippableFact]
    public async Task SnapshotCompositeKey_DatabaseEnforcement()
    {
        SkipIfNotAvailable();

        var snapshotStore = CreateSnapshotStore();
        var streamId = new StreamIdentifier(AggregateType, "snapshot-pk");

        // Save initial snapshot
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, 5),
            CreateProductCreatedEvent("Snapshot1", 100m));

        // Update snapshot (should replace due to PK constraint)
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, 10),
            CreateProductCreatedEvent("Snapshot2", 200m));

        // Verify only one snapshot exists in database
        using var context = CreateContext();
        var count = await context.Snapshots
            .Where(s => s.StreamType == AggregateType && s.StreamIdentifier == "snapshot-pk")
            .CountAsync();

        Assert.Equal(1, count);
    }

    [SkippableFact]
    public async Task DefaultValueSql_CreatedAtTimestamp()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();
        var before = DateTime.UtcNow;

        await store.AppendAsync(new StreamPointer(new StreamIdentifier(AggregateType, "timestamp"), 0),
            new List<AppendEvent>
            {
                new AppendEvent(CreateProductCreatedEvent("Test", 50m), null)
            });

        var after = DateTime.UtcNow;

        // Verify timestamp was set by database
        using var context = CreateContext();
        var eventEntity = await context.Events
            .FirstAsync(e => e.StreamType == AggregateType && e.StreamIdentifier == "timestamp");

        Assert.True(eventEntity.CreatedAt >= before);
        Assert.True(eventEntity.CreatedAt <= after);
    }

    [SkippableFact]
    public async Task LargePayload_HandlesVarcharMax()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();

        // Create event with large payload
        var largeDescription = new string('X', 10000);
        var largeEvent = CreateProductCreatedEvent(largeDescription, 999m);

        var result = await store.AppendAsync(
            new StreamPointer(new StreamIdentifier(AggregateType, "large-payload"), 0),
            new List<AppendEvent> { new AppendEvent(largeEvent, null) });

        Assert.Single(result);

        // Reload and verify
        var loaded = new List<StreamEvent>();
        await foreach (var evt in store.LoadAsync(new StreamPointer(new StreamIdentifier(AggregateType, "large-payload"), 0)))
        {
            loaded.Add(evt);
        }

        Assert.Single(loaded);
        AssertProductCreatedEvent(loaded[0].Event, largeDescription, 999m);
    }
}

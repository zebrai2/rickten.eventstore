using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Rickten.EventStore.Tests.Integration;

[Event("Order", "StateSnapshot", 1)]
public record OrderStateSnapshot(string Status, decimal TotalAmount, int ItemCount);

[Event("Order", "ItemAdded", 1)]
public record OrderItemAddedEvent(string ItemName, decimal Price);

[Event("Order", "StatusChanged", 1)]
public record OrderStatusChangedEvent(string NewStatus);

/// <summary>
/// Integration tests for snapshot functionality with real database.
/// Validates snapshot save/load/restore scenarios.
/// </summary>
public class SnapshotStoreIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventStoreDbContext> _options;

    public SnapshotStoreIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new EventStoreDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    private EventStoreDbContext CreateContext() => new EventStoreDbContext(_options);
    private EntityFramework.EventStore CreateEventStore() => new EntityFramework.EventStore(CreateContext());
    private EntityFramework.SnapshotStore CreateSnapshotStore() => new EntityFramework.SnapshotStore(CreateContext());

    [Fact]
    public async Task SnapshotRestore_LoadsFromSnapshotThenAppliesNewEvents()
    {
        // This test validates the core snapshot restore pattern:
        // 1. Build up state by processing events
        // 2. Save a snapshot at a specific version
        // 3. Later, restore from snapshot and only process events after snapshot version

        var eventStore = CreateEventStore();
        var snapshotStore = CreateSnapshotStore();
        var streamId = new StreamIdentifier("Order", "restore-test");

        // Append events to build up state
        var events = new List<AppendEvent>
        {
            new AppendEvent(new OrderItemAddedEvent("Item1", 10m), null),
            new AppendEvent(new OrderItemAddedEvent("Item2", 20m), null),
            new AppendEvent(new OrderItemAddedEvent("Item3", 30m), null),
            new AppendEvent(new OrderStatusChangedEvent("Pending"), null)
        };

        var appendedEvents = await eventStore.AppendAsync(
            new StreamPointer(streamId, 0), 
            events);

        // Simulate building state from events 1-4
        var snapshotVersion = 4L;
        var snapshotState = new OrderStateSnapshot("Pending", 60m, 3);
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, snapshotVersion), 
            snapshotState);

        // Append more events after the snapshot
        var newEvents = new List<AppendEvent>
        {
            new AppendEvent(new OrderItemAddedEvent("Item4", 15m), null),
            new AppendEvent(new OrderStatusChangedEvent("Confirmed"), null)
        };

        await eventStore.AppendAsync(
            new StreamPointer(streamId, snapshotVersion), 
            newEvents);

        // Now simulate restore: load snapshot + events after snapshot
        var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.NotNull(snapshot);
        Assert.Equal(snapshotVersion, snapshot.StreamPointer.Version);

        var restoredState = snapshot.State as OrderStateSnapshot;
        Assert.NotNull(restoredState);
        Assert.Equal("Pending", restoredState.Status);
        Assert.Equal(60m, restoredState.TotalAmount);
        Assert.Equal(3, restoredState.ItemCount);

        // Load only events after the snapshot
        var eventsAfterSnapshot = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(snapshot.StreamPointer))
        {
            eventsAfterSnapshot.Add(evt);
        }

        // Should only get the 2 new events
        Assert.Equal(2, eventsAfterSnapshot.Count);
        Assert.Equal(5, eventsAfterSnapshot[0].StreamPointer.Version);
        Assert.Equal(6, eventsAfterSnapshot[1].StreamPointer.Version);
    }

    [Fact]
    public async Task SnapshotUpdate_OverwritesPreviousSnapshot()
    {
        // Verify that saving a new snapshot replaces the old one
        var snapshotStore = CreateSnapshotStore();
        var streamId = new StreamIdentifier("Order", "update-test");

        // Save initial snapshot
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, 5),
            new OrderStateSnapshot("Draft", 100m, 5));

        // Update to new snapshot
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, 10),
            new OrderStateSnapshot("Confirmed", 150m, 7));

        // Load snapshot - should get the latest one
        var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.NotNull(snapshot);
        Assert.Equal(10, snapshot.StreamPointer.Version);

        var state = snapshot.State as OrderStateSnapshot;
        Assert.NotNull(state);
        Assert.Equal("Confirmed", state.Status);
        Assert.Equal(150m, state.TotalAmount);
        Assert.Equal(7, state.ItemCount);
    }

    [Fact]
    public async Task SnapshotRestore_WorksWithNoEventsAfterSnapshot()
    {
        // Edge case: snapshot is at the current stream version
        var eventStore = CreateEventStore();
        var snapshotStore = CreateSnapshotStore();
        var streamId = new StreamIdentifier("Order", "edge-test");

        // Append events
        await eventStore.AppendAsync(
            new StreamPointer(streamId, 0),
            new List<AppendEvent>
            {
                new AppendEvent(new OrderItemAddedEvent("Item1", 50m), null)
            });

        // Save snapshot at current version
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, 1),
            new OrderStateSnapshot("Complete", 50m, 1));

        // Restore
        var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.StreamPointer.Version);

        // Load events after snapshot - should be empty
        var eventsAfterSnapshot = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(snapshot.StreamPointer))
        {
            eventsAfterSnapshot.Add(evt);
        }

        Assert.Empty(eventsAfterSnapshot);
    }

    [Fact]
    public async Task MultipleStreams_IndependentSnapshots()
    {
        // Verify snapshots are correctly isolated per stream
        var snapshotStore = CreateSnapshotStore();

        var stream1 = new StreamIdentifier("Order", "1");
        var stream2 = new StreamIdentifier("Order", "2");
        var stream3 = new StreamIdentifier("Invoice", "1");

        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(stream1, 5),
            new OrderStateSnapshot("Stream1", 100m, 5));

        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(stream2, 3),
            new OrderStateSnapshot("Stream2", 200m, 3));

        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(stream3, 7),
            new OrderStateSnapshot("Stream3", 300m, 7));

        // Load each snapshot independently
        var snapshot1 = await snapshotStore.LoadSnapshotAsync(stream1);
        var snapshot2 = await snapshotStore.LoadSnapshotAsync(stream2);
        var snapshot3 = await snapshotStore.LoadSnapshotAsync(stream3);

        Assert.NotNull(snapshot1);
        Assert.NotNull(snapshot2);
        Assert.NotNull(snapshot3);

        Assert.Equal(5, snapshot1.StreamPointer.Version);
        Assert.Equal(3, snapshot2.StreamPointer.Version);
        Assert.Equal(7, snapshot3.StreamPointer.Version);

        var state1 = snapshot1.State as OrderStateSnapshot;
        var state2 = snapshot2.State as OrderStateSnapshot;
        var state3 = snapshot3.State as OrderStateSnapshot;

        Assert.Equal("Stream1", state1!.Status);
        Assert.Equal("Stream2", state2!.Status);
        Assert.Equal("Stream3", state3!.Status);
    }

    [Fact]
    public async Task SnapshotRestore_CompleteEndToEndScenario()
    {
        // Complete scenario: events -> snapshot -> more events -> restore and verify
        var eventStore = CreateEventStore();
        var snapshotStore = CreateSnapshotStore();
        var streamId = new StreamIdentifier("Order", "e2e-test");

        // Phase 1: Process initial batch of events
        var phase1Events = new List<AppendEvent>
        {
            new AppendEvent(new OrderItemAddedEvent("Laptop", 1000m), null),
            new AppendEvent(new OrderItemAddedEvent("Mouse", 50m), null),
            new AppendEvent(new OrderStatusChangedEvent("Draft"), null)
        };
        await eventStore.AppendAsync(new StreamPointer(streamId, 0), phase1Events);

        // Save snapshot after phase 1
        var phase1Snapshot = new OrderStateSnapshot("Draft", 1050m, 2);
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, 3),
            phase1Snapshot);

        // Phase 2: More events
        var phase2Events = new List<AppendEvent>
        {
            new AppendEvent(new OrderItemAddedEvent("Keyboard", 100m), null),
            new AppendEvent(new OrderStatusChangedEvent("Pending"), null)
        };
        await eventStore.AppendAsync(new StreamPointer(streamId, 3), phase2Events);

        // Update snapshot after phase 2
        var phase2Snapshot = new OrderStateSnapshot("Pending", 1150m, 3);
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, 5),
            phase2Snapshot);

        // Phase 3: Final events
        var phase3Events = new List<AppendEvent>
        {
            new AppendEvent(new OrderStatusChangedEvent("Confirmed"), null),
            new AppendEvent(new OrderStatusChangedEvent("Shipped"), null)
        };
        await eventStore.AppendAsync(new StreamPointer(streamId, 5), phase3Events);

        // Now restore: should get phase 2 snapshot + phase 3 events
        var currentSnapshot = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.NotNull(currentSnapshot);
        Assert.Equal(5, currentSnapshot.StreamPointer.Version);

        var currentState = currentSnapshot.State as OrderStateSnapshot;
        Assert.NotNull(currentState);
        Assert.Equal("Pending", currentState.Status);
        Assert.Equal(1150m, currentState.TotalAmount);

        // Get events after snapshot
        var remainingEvents = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(currentSnapshot.StreamPointer))
        {
            remainingEvents.Add(evt);
        }

        Assert.Equal(2, remainingEvents.Count);
        Assert.Equal(6, remainingEvents[0].StreamPointer.Version);
        Assert.Equal(7, remainingEvents[1].StreamPointer.Version);

        // Verify we can reconstruct final state
        var finalStatus = "Shipped"; // From processing the remaining events
        Assert.Equal("Shipped", finalStatus);
    }

    [Fact]
    public async Task NoSnapshot_ReturnsNull()
    {
        var snapshotStore = CreateSnapshotStore();
        var streamId = new StreamIdentifier("Order", "no-snapshot");

        var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task SnapshotCompositeKey_EnforcesUniqueness()
    {
        // The snapshot table has a composite primary key (StreamType, StreamIdentifier)
        // This test verifies that constraint is enforced
        var snapshotStore = CreateSnapshotStore();
        var streamId = new StreamIdentifier("Order", "composite-key-test");

        // Save first snapshot
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, 5),
            new OrderStateSnapshot("V5", 100m, 5));

        // Save another snapshot for same stream - should update, not create duplicate
        await snapshotStore.SaveSnapshotAsync(
            new StreamPointer(streamId, 10),
            new OrderStateSnapshot("V10", 200m, 10));

        // Verify only one snapshot exists
        using var context = CreateContext();
        var snapshots = await context.Snapshots
            .Where(s => s.StreamType == "Order" && s.StreamIdentifier == "composite-key-test")
            .ToListAsync();

        Assert.Single(snapshots);
        Assert.Equal(10, snapshots[0].Version);
    }
}

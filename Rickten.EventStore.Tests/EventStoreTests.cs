using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using Rickten.EventStore.TypeMetadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rickten.EventStore.Tests;

[Event("Order", "Created", 1)]
public record OrderCreatedEvent(decimal Amount);

[Event("Order", "Updated", 1)]
public record OrderUpdatedEvent(string Status);

[Event("Invoice", "Created", 1)]
public record InvoiceCreatedEvent(decimal Total);

public class EventStoreTests
{
    private static readonly ITypeMetadataRegistry Registry = TestTypeMetadataRegistry.Create();

    private EventStoreDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new EventStoreDbContext(options);
    }

    private EventStore CreateStore(string dbName) => new EventStore(CreateContext(dbName), Registry);

    private StreamPointer MakePointer(string streamType, string streamId, long version) =>
        new StreamPointer(new StreamIdentifier(streamType, streamId), version);

    [Fact]
    public async Task AppendAndLoadEvents_Success()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "1", 0);
        var appendEvents = new List<AppendEvent>
        {
            new AppendEvent(new OrderCreatedEvent(100), Array.Empty<AppendMetadata>())
        };
        var result = await store.AppendAsync(pointer, appendEvents);
        Assert.Single(result);
        Assert.Equal(1, result[0].StreamPointer.Version);

        // Load
        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(MakePointer("Order", "1", 0)))
            loaded.Add(e);
        Assert.Single(loaded);
        Assert.Equal(1, loaded[0].StreamPointer.Version);
    }

    [Fact]
    public async Task AppendAsync_ThrowsOnVersionConflict()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "2", 0);
        await store.AppendAsync(pointer, new List<AppendEvent> { new AppendEvent(new OrderCreatedEvent(100), null) });
        // Try to append at same version
        await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
        {
            await store.AppendAsync(pointer, new List<AppendEvent> { new AppendEvent(new OrderCreatedEvent(200), null) });
        });
    }

    [Fact]
    public async Task LoadAllAsync_FiltersAndOrders()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer1 = MakePointer("Order", "3", 0);
        var pointer2 = MakePointer("Invoice", "4", 0);
        await store.AppendAsync(pointer1, new List<AppendEvent> { new AppendEvent(new OrderCreatedEvent(100), null) });
        await store.AppendAsync(pointer2, new List<AppendEvent> { new AppendEvent(new InvoiceCreatedEvent(200), null) });
        var all = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync())
            all.Add(e);
        Assert.Equal(2, all.Count);
        // Filter by stream type
        var filtered = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(0, new[] { "Order" }))
            filtered.Add(e);
        Assert.Single(filtered);
        Assert.Equal("Order", filtered[0].StreamPointer.Stream.StreamType);

        // Filter by event type using wire name
        var eventFiltered = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(0, null, new[] { "Invoice.Created.v1" }))
            eventFiltered.Add(e);
        Assert.Single(eventFiltered);
        Assert.IsType<InvoiceCreatedEvent>(eventFiltered[0].Event);
    }

    [Fact]
    public async Task AppendAsync_EmptyEvents_ReturnsEmpty()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "5", 0);
        var result = await store.AppendAsync(pointer, new List<AppendEvent>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task AppendAsync_ThrowsWhenEventAggregateMismatchesStreamType()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        // Try to append an Invoice event to an Order stream
        var orderStream = MakePointer("Order", "6", 0);
        var invoiceEvent = new AppendEvent(new InvoiceCreatedEvent(100), null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await store.AppendAsync(orderStream, new List<AppendEvent> { invoiceEvent });
        });

        Assert.Contains("Event aggregate 'Invoice' does not match stream type 'Order'", exception.Message);
    }

    [Fact]
    public async Task AppendAsync_AllowsMatchingEventAggregate()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        // Append Order events to Order stream - should succeed
        var orderStream = MakePointer("Order", "7", 0);
        var orderEvents = new List<AppendEvent>
        {
            new AppendEvent(new OrderCreatedEvent(100), null),
            new AppendEvent(new OrderUpdatedEvent("Confirmed"), null)
        };

        var result = await store.AppendAsync(orderStream, orderEvents);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].StreamPointer.Version);
        Assert.Equal(2, result[1].StreamPointer.Version);
    }

    [Fact]
    public async Task AppendAsync_ThrowsOnFirstMismatchInBatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        // Mix valid and invalid events
        var orderStream = MakePointer("Order", "8", 0);
        var mixedEvents = new List<AppendEvent>
        {
            new AppendEvent(new OrderCreatedEvent(100), null),
            new AppendEvent(new InvoiceCreatedEvent(200), null) // This should fail
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await store.AppendAsync(orderStream, mixedEvents);
        });

        Assert.Contains("Event aggregate 'Invoice' does not match stream type 'Order'", exception.Message);

        // Verify no events were persisted
        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(orderStream))
            loaded.Add(e);
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task AppendAsync_AllowsInvoiceEventsToInvoiceStream()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        // Append Invoice event to Invoice stream - should succeed
        var invoiceStream = MakePointer("Invoice", "9", 0);
        var invoiceEvent = new AppendEvent(new InvoiceCreatedEvent(500), null);

        var result = await store.AppendAsync(invoiceStream, new List<AppendEvent> { invoiceEvent });

        Assert.Single(result);
        Assert.Equal(1, result[0].StreamPointer.Version);
    }

    [Fact]
    public async Task AppendAsync_WithMetadata_StoresAndRetrievesMetadata()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "10", 0);

        var correlationId = Guid.NewGuid().ToString();
        var userId = "user-123";
        var requestId = "req-456";

        var appendEvents = new List<AppendEvent>
        {
            new AppendEvent(
                new OrderCreatedEvent(100),
                new[]
                {
                    new AppendMetadata("CorrelationId", correlationId),
                    new AppendMetadata("UserId", userId),
                    new AppendMetadata("RequestId", requestId)
                })
        };

        var result = await store.AppendAsync(pointer, appendEvents);
        Assert.Single(result);

        // Load and verify metadata
        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(MakePointer("Order", "10", 0)))
            loaded.Add(e);

        Assert.Single(loaded);
        var metadata = loaded[0].Metadata;

        // Should have 3 client metadata + 2 system metadata (Timestamp, StreamVersion)
        Assert.Equal(5, metadata.Count);

        // Verify client metadata (automatically tagged as "Client")
        Assert.Equal("Client", metadata[0].Source);
        Assert.Equal("CorrelationId", metadata[0].Key);
        Assert.Equal(correlationId, metadata[0].Value?.ToString());

        Assert.Equal("Client", metadata[1].Source);
        Assert.Equal("UserId", metadata[1].Key);
        Assert.Equal(userId, metadata[1].Value?.ToString());

        Assert.Equal("Client", metadata[2].Source);
        Assert.Equal("RequestId", metadata[2].Key);
        Assert.Equal(requestId, metadata[2].Value?.ToString());

        // Verify system metadata (automatically added)
        Assert.Equal("System", metadata[3].Source);
        Assert.Equal("Timestamp", metadata[3].Key);
        Assert.NotNull(metadata[3].Value);

        Assert.Equal("System", metadata[4].Source);
        Assert.Equal("StreamVersion", metadata[4].Key);
        Assert.Equal("1", metadata[4].Value?.ToString()); // First event, version 1
    }

    [Fact]
    public async Task AppendAsync_ToExistingStream_ReturnsOnlyNewEvents()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var streamId = new StreamIdentifier("Order", "11");

        // First append - create stream with 2 events
        var pointer1 = new StreamPointer(streamId, 0);
        var firstBatch = new List<AppendEvent>
        {
            new AppendEvent(new OrderCreatedEvent(100), null),
            new AppendEvent(new OrderUpdatedEvent("Pending"), null)
        };
        var result1 = await store.AppendAsync(pointer1, firstBatch);
        Assert.Equal(2, result1.Count);
        Assert.Equal(1, result1[0].StreamPointer.Version);
        Assert.Equal(2, result1[1].StreamPointer.Version);

        // Second append - add 2 more events
        var pointer2 = new StreamPointer(streamId, 2); // Current version is 2
        var secondBatch = new List<AppendEvent>
        {
            new AppendEvent(new OrderUpdatedEvent("Processing"), null),
            new AppendEvent(new OrderUpdatedEvent("Completed"), null)
        };
        var result2 = await store.AppendAsync(pointer2, secondBatch);

        // Should return ONLY the 2 new events (versions 3 and 4), not the existing version 2
        Assert.Equal(2, result2.Count);
        Assert.Equal(3, result2[0].StreamPointer.Version);
        Assert.Equal(4, result2[1].StreamPointer.Version);

        // Verify no duplicate of version 2
        Assert.DoesNotContain(result2, e => e.StreamPointer.Version == 2);
    }

    [Fact]
    public async Task LoadAsync_FromMidStream_DoesNotReplayPreviousVersion()
    {
        // This test verifies the off-by-one bug fix in snapshot replay
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var streamId = new StreamIdentifier("Order", "12");

        // Create a stream with 5 events
        var pointer = new StreamPointer(streamId, 0);
        var events = new List<AppendEvent>
        {
            new AppendEvent(new OrderCreatedEvent(100), null),
            new AppendEvent(new OrderUpdatedEvent("Pending"), null),
            new AppendEvent(new OrderUpdatedEvent("Processing"), null),
            new AppendEvent(new OrderUpdatedEvent("Shipped"), null),
            new AppendEvent(new OrderUpdatedEvent("Delivered"), null)
        };
        await store.AppendAsync(pointer, events);

        // Load from version 3 (simulating a snapshot at version 3)
        // Should load versions 4 and 5 only, NOT version 3
        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(new StreamPointer(streamId, 3)))
            loaded.Add(e);

        // Should load exactly 2 events (versions 4 and 5)
        Assert.Equal(2, loaded.Count);
        Assert.Equal(4, loaded[0].StreamPointer.Version);
        Assert.Equal(5, loaded[1].StreamPointer.Version);

        // Verify version 3 is NOT included (this would be the bug)
        Assert.DoesNotContain(loaded, e => e.StreamPointer.Version == 3);
    }

    [Fact]
    public async Task LoadAllAsync_FromCheckpoint_DoesNotReplayCheckpointEvent()
    {
        // This test verifies that LoadAllAsync uses exclusive semantics:
        // If fromGlobalPosition is N, it should load events with global position > N,
        // not >= N. This prevents double-processing when resuming from a checkpoint.

        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        // Create multiple events across different streams
        var stream1 = new StreamIdentifier("Order", "checkpoint-test-1");
        var stream2 = new StreamIdentifier("Order", "checkpoint-test-2");
        var stream3 = new StreamIdentifier("Invoice", "checkpoint-test-3");

        await store.AppendAsync(new StreamPointer(stream1, 0), new List<AppendEvent>
        {
            new AppendEvent(new OrderCreatedEvent(100), null),
            new AppendEvent(new OrderUpdatedEvent("Pending"), null)
        });

        await store.AppendAsync(new StreamPointer(stream2, 0), new List<AppendEvent>
        {
            new AppendEvent(new OrderCreatedEvent(200), null)
        });

        await store.AppendAsync(new StreamPointer(stream3, 0), new List<AppendEvent>
        {
            new AppendEvent(new InvoiceCreatedEvent(300), null),
            new AppendEvent(new InvoiceCreatedEvent(400), null)
        });

        // Load all events to get their global positions
        var allEvents = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(0))
            allEvents.Add(e);

        Assert.Equal(5, allEvents.Count);

        // Simulate a checkpoint: we've processed up to the 3rd event (global position N)
        var checkpointPosition = allEvents[2].GlobalPosition;

        // Now resume from checkpoint - should load events AFTER position N (exclusive)
        var resumedEvents = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(checkpointPosition))
            resumedEvents.Add(e);

        // Should get only the last 2 events (indices 3 and 4)
        Assert.Equal(2, resumedEvents.Count);
        Assert.Equal(allEvents[3].GlobalPosition, resumedEvents[0].GlobalPosition);
        Assert.Equal(allEvents[4].GlobalPosition, resumedEvents[1].GlobalPosition);

        // Critical: verify the checkpoint event itself is NOT included (exclusive semantics)
        Assert.DoesNotContain(resumedEvents, e => e.GlobalPosition == checkpointPosition);

        // All loaded events should have global position > checkpoint
        Assert.All(resumedEvents, e => Assert.True(e.GlobalPosition > checkpointPosition));
    }
}


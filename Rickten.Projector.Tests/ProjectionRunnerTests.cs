using Microsoft.Extensions.DependencyInjection;
using Rickten.EventStore;
using Xunit;

namespace Rickten.Projector.Tests;

[Event("TestOrder", "Created", 1)]
public record TestOrderCreatedEvent(string OrderId, decimal Amount);

[Event("TestOrder", "Updated", 1)]
public record TestOrderUpdatedEvent(string OrderId, string Status);

[Event("OtherAggregate", "Created", 1)]
public record OtherAggregateCreatedEvent(string Id, string Data);

[Projection("OrderCounter", AggregateTypes = new[] { "TestOrder" })]
public class OrderCounterProjection : Projection<int>
{
    public override int InitialView() => 0;
    protected override int ApplyEvent(int view, StreamEvent streamEvent) => view + 1;
}

public class ProjectionRunnerTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly IServiceProvider _serviceProvider;

    public ProjectionRunnerTests()
    {
        (_connection, _serviceProvider) = TestServiceFactory.CreateServiceProvider();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task RebuildAsync_FromCheckpoint_DoesNotReplayCheckpointEvent()
    {
        // This test verifies that RebuildAsync correctly uses exclusive checkpoint semantics:
        // When resuming from checkpoint N, it should process events with global position > N,
        // not >= N, to avoid double-processing the checkpoint event.

        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var projection = new OrderCounterProjection();

        // Create initial events
        var stream1 = new StreamIdentifier("TestOrder", "rebuild-1");
        var stream2 = new StreamIdentifier("TestOrder", "rebuild-2");

        await eventStore.AppendAsync(new StreamPointer(stream1, 0), new List<AppendEvent>
        {
            new AppendEvent(new TestOrderCreatedEvent("order-1", 100m), null),
            new AppendEvent(new TestOrderUpdatedEvent("order-1", "Pending"), null)
        });

        await eventStore.AppendAsync(new StreamPointer(stream2, 0), new List<AppendEvent>
        {
            new AppendEvent(new TestOrderCreatedEvent("order-2", 200m), null)
        });

        // Simulate first rebuild: process all events and save checkpoint
        var (firstView, firstCheckpoint) = await ProjectionRunner.RebuildAsync(
            eventStore,
            projection,
            fromGlobalPosition: 0);

        Assert.Equal(3, firstView); // 3 events processed
        Assert.True(firstCheckpoint > 0);

        // Add more events after checkpoint
        await eventStore.AppendAsync(new StreamPointer(stream1, 2), new List<AppendEvent>
        {
            new AppendEvent(new TestOrderUpdatedEvent("order-1", "Confirmed"), null)
        });

        await eventStore.AppendAsync(new StreamPointer(stream2, 1), new List<AppendEvent>
        {
            new AppendEvent(new TestOrderUpdatedEvent("order-2", "Shipped"), null)
        });

        // Rebuild from checkpoint - should ONLY process new events (2 events)
        // NOT the checkpoint event itself
        var (secondView, secondCheckpoint) = await ProjectionRunner.RebuildAsync(
            eventStore,
            projection,
            fromGlobalPosition: firstCheckpoint);

        // Critical assertion: should count only 2 new events, not 3
        // If it counted 3, it would have replayed the checkpoint event
        Assert.Equal(2, secondView);
        Assert.True(secondCheckpoint > firstCheckpoint);
    }

    [Fact]
    public async Task CatchUpAsync_FromCheckpoint_DoesNotReplayCheckpointEvent()
    {
        // This test verifies that CatchUpAsync correctly handles checkpoints:
        // After loading a checkpoint at position N and processing new events,
        // it should only apply events with global position > N.

        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var projectionStore = _serviceProvider.GetRequiredService<IProjectionStore>();
        var projection = new OrderCounterProjection();

        var stream = new StreamIdentifier("TestOrder", "catchup-1");

        // Create initial events
        await eventStore.AppendAsync(new StreamPointer(stream, 0), new List<AppendEvent>
        {
            new AppendEvent(new TestOrderCreatedEvent("order-1", 100m), null),
            new AppendEvent(new TestOrderUpdatedEvent("order-1", "Pending"), null),
            new AppendEvent(new TestOrderUpdatedEvent("order-1", "Confirmed"), null)
        });

        // First catch-up: process all events
        var (firstView, firstCheckpoint) = await ProjectionRunner.CatchUpAsync(
            eventStore,
            projectionStore,
            projection,
            "OrderCounter");

        Assert.Equal(3, firstView); // 3 events processed
        Assert.True(firstCheckpoint > 0);

        // Verify checkpoint was saved
        var savedCheckpoint = await projectionStore.LoadProjectionAsync<int>("OrderCounter");
        Assert.NotNull(savedCheckpoint);
        Assert.Equal(3, savedCheckpoint.State);
        Assert.Equal(firstCheckpoint, savedCheckpoint.GlobalPosition);

        // Add more events
        await eventStore.AppendAsync(new StreamPointer(stream, 3), new List<AppendEvent>
        {
            new AppendEvent(new TestOrderUpdatedEvent("order-1", "Shipped"), null),
            new AppendEvent(new TestOrderUpdatedEvent("order-1", "Delivered"), null)
        });

        // Second catch-up: should resume from checkpoint and process only new events
        var (secondView, secondCheckpoint) = await ProjectionRunner.CatchUpAsync(
            eventStore,
            projectionStore,
            projection,
            "OrderCounter");

        // Critical assertion: should be 5 total (3 from before + 2 new)
        // If it replayed the checkpoint event, it would be 6
        Assert.Equal(5, secondView);
        Assert.True(secondCheckpoint > firstCheckpoint);

        // Verify final checkpoint
        var finalCheckpoint = await projectionStore.LoadProjectionAsync<int>("OrderCounter");
        Assert.NotNull(finalCheckpoint);
        Assert.Equal(5, finalCheckpoint.State);
        Assert.Equal(secondCheckpoint, finalCheckpoint.GlobalPosition);
    }

    [Fact]
    public async Task CatchUpAsync_NoNewEvents_DoesNotUpdateCheckpoint()
    {
        // Verifies that if no new events exist after the checkpoint,
        // the checkpoint is not updated (saves unnecessary writes).

        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var projectionStore = _serviceProvider.GetRequiredService<IProjectionStore>();
        var projection = new OrderCounterProjection();

        var stream = new StreamIdentifier("TestOrder", "no-update-1");

        // Create events
        await eventStore.AppendAsync(new StreamPointer(stream, 0), new List<AppendEvent>
        {
            new AppendEvent(new TestOrderCreatedEvent("order-1", 100m), null)
        });

        // First catch-up
        var (firstView, firstCheckpoint) = await ProjectionRunner.CatchUpAsync(
            eventStore,
            projectionStore,
            projection,
            "NoUpdateTest");

        Assert.Equal(1, firstView);
        Assert.True(firstCheckpoint > 0);

        // Second catch-up with no new events
        var (secondView, secondCheckpoint) = await ProjectionRunner.CatchUpAsync(
            eventStore,
            projectionStore,
            projection,
            "NoUpdateTest");

        // Should return same view and checkpoint
        Assert.Equal(1, secondView);
        Assert.Equal(firstCheckpoint, secondCheckpoint);
    }

    [Fact]
    public async Task RebuildAsync_WithFilters_ProcessesOnlyMatchingEvents()
    {
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var projection = new OrderCounterProjection(); // Filtered to TestOrder aggregate

        // Create events for TestOrder
        var orderStream = new StreamIdentifier("TestOrder", "filtered-1");
        await eventStore.AppendAsync(new StreamPointer(orderStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new TestOrderCreatedEvent("order-1", 100m), null),
            new AppendEvent(new TestOrderUpdatedEvent("order-1", "Pending"), null)
        });

        // Create events for a different aggregate type (should be filtered out)
        var otherStream = new StreamIdentifier("OtherAggregate", "filtered-2");
        await eventStore.AppendAsync(new StreamPointer(otherStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new OtherAggregateCreatedEvent("other-1", "some data"), null)
        });

        var (view, _) = await ProjectionRunner.RebuildAsync(
            eventStore,
            projection,
            fromGlobalPosition: 0);

        // Should count only the 2 TestOrder events, not the OtherAggregate event
        Assert.Equal(2, view);
    }
}

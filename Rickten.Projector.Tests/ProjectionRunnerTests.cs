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

[Projection("OrderCounter")]
public class OrderCounterState
{
    public int Count { get; set; }
}

[Projection("OrderCounter", AggregateTypes = new[] { "TestOrder" })]
public class OrderCounterProjection : Projection<OrderCounterState>
{
    public override OrderCounterState InitialView() => new() { Count = 0 };
    protected override OrderCounterState ApplyEvent(OrderCounterState view, StreamEvent streamEvent)
    {
        return new OrderCounterState { Count = view.Count + 1 };
    }
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
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RebuildAsync_FromCheckpoint_DoesNotReplayCheckpointEvent()
    {
        // This test verifies that RebuildAsync correctly uses exclusive checkpoint semantics:
        // When resuming from checkpoint N, it should process events with global position > N,
        // not >= N, to avoid double-processing the checkpoint event.

        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var runner = _serviceProvider.GetRequiredService<ProjectionRunner>();
        var projection = new OrderCounterProjection();

        // Create initial events
        var stream1 = new StreamIdentifier("TestOrder", "rebuild-1");
        var stream2 = new StreamIdentifier("TestOrder", "rebuild-2");

        await eventStore.AppendAsync(new StreamPointer(stream1, 0),
        [
            new(new TestOrderCreatedEvent("order-1", 100m), null),
            new(new TestOrderUpdatedEvent("order-1", "Pending"), null)
        ]);

        await eventStore.AppendAsync(new StreamPointer(stream2, 0),
        [
            new(new TestOrderCreatedEvent("order-2", 200m), null)
        ]);

        // Simulate first rebuild: process all events and save checkpoint
        var (firstView, firstCheckpoint) = await runner.RebuildAsync(
            projection,
            fromGlobalPosition: 0);

        Assert.Equal(3, firstView.Count); // 3 events processed
        Assert.True(firstCheckpoint > 0);

        // Add more events after checkpoint
        await eventStore.AppendAsync(new StreamPointer(stream1, 2),
        [
            new(new TestOrderUpdatedEvent("order-1", "Confirmed"), null)
        ]);

        await eventStore.AppendAsync(new StreamPointer(stream2, 1),
        [
            new(new TestOrderUpdatedEvent("order-2", "Shipped"), null)
        ]);

        // Rebuild from checkpoint - should ONLY process new events (2 events)
        // NOT the checkpoint event itself
        var (secondView, secondCheckpoint) = await runner.RebuildAsync(
            projection,
            firstCheckpoint);

        // Critical assertion: should count only 2 new events, not 3
        // If it counted 3, it would have replayed the checkpoint event
        Assert.Equal(2, secondView.Count);
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
        var runner = _serviceProvider.GetRequiredService<ProjectionRunner>();
        var projection = new OrderCounterProjection();

        var stream = new StreamIdentifier("TestOrder", "catchup-1");

        // Create initial events
        await eventStore.AppendAsync(new StreamPointer(stream, 0),
        [
            new(new TestOrderCreatedEvent("order-1", 100m), null),
            new(new TestOrderUpdatedEvent("order-1", "Pending"), null),
            new(new TestOrderUpdatedEvent("order-1", "Confirmed"), null)
        ]);

        // First catch-up: process all events
        var (firstView, firstCheckpoint) = await runner.CatchUpAsync(projection);

        Assert.Equal(3, firstView.Count); // 3 events processed
        Assert.True(firstCheckpoint > 0);

        // Verify checkpoint was saved
        var savedCheckpoint = await projectionStore.LoadProjectionAsync<OrderCounterState>("OrderCounter");
        Assert.NotNull(savedCheckpoint);
        Assert.Equal(3, savedCheckpoint.State.Count);
        Assert.Equal(firstCheckpoint, savedCheckpoint.GlobalPosition);

        // Add more events
        await eventStore.AppendAsync(new StreamPointer(stream, 3),
        [
            new(new TestOrderUpdatedEvent("order-1", "Shipped"), null),
            new(new TestOrderUpdatedEvent("order-1", "Delivered"), null)
        ]);

        // Second catch-up: should resume from checkpoint and process only new events
        var (secondView, secondCheckpoint) = await runner.CatchUpAsync(projection);

        // Critical assertion: should be 5 total (3 from before + 2 new)
        // If it replayed the checkpoint event, it would be 6
        Assert.Equal(5, secondView.Count);
        Assert.True(secondCheckpoint > firstCheckpoint);

        // Verify final checkpoint
        var finalCheckpoint = await projectionStore.LoadProjectionAsync<OrderCounterState>("OrderCounter");
        Assert.NotNull(finalCheckpoint);
        Assert.Equal(5, finalCheckpoint.State.Count);
        Assert.Equal(secondCheckpoint, finalCheckpoint.GlobalPosition);
    }

    [Fact]
    public async Task CatchUpAsync_NoNewEvents_DoesNotUpdateCheckpoint()
    {
        // Verifies that if no new events exist after the checkpoint,
        // the checkpoint is not updated (saves unnecessary writes).

        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var projectionStore = _serviceProvider.GetRequiredService<IProjectionStore>();
        var runner = _serviceProvider.GetRequiredService<ProjectionRunner>();
        var projection = new OrderCounterProjection();

        var stream = new StreamIdentifier("TestOrder", "no-update-1");

        // Create events
        await eventStore.AppendAsync(new StreamPointer(stream, 0),
        [
            new(new TestOrderCreatedEvent("order-1", 100m), null)
        ]);

        // First catch-up
        var (firstView, firstCheckpoint) = await runner.CatchUpAsync(
            projection,
            "NoUpdateTest");

        Assert.Equal(1, firstView.Count);
        Assert.True(firstCheckpoint > 0);

        // Second catch-up with no new events (same namespace)
        var (secondView, secondCheckpoint) = await runner.CatchUpAsync(
            projection,
            "NoUpdateTest");

        // Should return same view and checkpoint
        Assert.Equal(1, secondView.Count);
        Assert.Equal(firstCheckpoint, secondCheckpoint);
    }

    [Fact]
    public async Task RebuildAsync_WithFilters_ProcessesOnlyMatchingEvents()
    {
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var runner = _serviceProvider.GetRequiredService<ProjectionRunner>();
        var projection = new OrderCounterProjection(); // Filtered to TestOrder aggregate

        // Create events for TestOrder
        var orderStream = new StreamIdentifier("TestOrder", "filtered-1");
        await eventStore.AppendAsync(new StreamPointer(orderStream, 0),
        [
            new(new TestOrderCreatedEvent("order-1", 100m), null),
            new(new TestOrderUpdatedEvent("order-1", "Pending"), null)
        ]);

        // Create events for a different aggregate type (should be filtered out)
        var otherStream = new StreamIdentifier("OtherAggregate", "filtered-2");
        await eventStore.AppendAsync(new StreamPointer(otherStream, 0),
        [
            new(new OtherAggregateCreatedEvent("other-1", "some data"), null)
        ]);

        var (view, _) = await runner.RebuildAsync(
            projection,
            0);

        // Should count only the 2 TestOrder events, not the OtherAggregate event
        Assert.Equal(2, view.Count);
    }

    [Fact]
    public async Task RebuildAsync_WithEventTypeFilter_ProcessesOnlyMatchingEvents()
    {
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var runner = _serviceProvider.GetRequiredService<ProjectionRunner>();
        var projection = new EventTypeFilteredProjection(); // Filtered to TestOrder.Created.v1

        var orderStream = new StreamIdentifier("TestOrder", "event-filtered-1");
        await eventStore.AppendAsync(new StreamPointer(orderStream, 0),
        [
            new(new TestOrderCreatedEvent("order-1", 100m), null),
            new(new TestOrderUpdatedEvent("order-1", "Pending"), null),
            new(new TestOrderCreatedEvent("order-2", 200m), null)
        ]);

        var (view, _) = await runner.RebuildAsync(
            projection,
            0);

        // Should count only the 2 Created events, not the Updated event
        Assert.Equal(2, view.Count);
    }

    [Fact]
    public async Task RebuildAsync_WithBothFilters_ProcessesOnlyMatchingEvents()
    {
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var runner = _serviceProvider.GetRequiredService<ProjectionRunner>();
        var projection = new BothFiltersProjection(); // Filtered to TestOrder aggregate AND Created events

        // TestOrder events - only Created should be processed
        var orderStream = new StreamIdentifier("TestOrder", "both-filtered-1");
        await eventStore.AppendAsync(new StreamPointer(orderStream, 0),
        [
            new(new TestOrderCreatedEvent("order-1", 100m), null),
            new(new TestOrderUpdatedEvent("order-1", "Pending"), null),
            new(new TestOrderCreatedEvent("order-2", 200m), null)
        ]);

        // OtherAggregate events - should be filtered out by aggregate type
        var otherStream = new StreamIdentifier("OtherAggregate", "both-filtered-2");
        await eventStore.AppendAsync(new StreamPointer(otherStream, 0),
        [
            new(new OtherAggregateCreatedEvent("other-1", "data"), null)
        ]);

        var (view, _) = await runner.RebuildAsync(
            projection,
            0);

        // Should count only the 2 TestOrder.Created events
        Assert.Equal(2, view.Count);
    }

    [Fact]
    public async Task CatchUpAsync_WithEventTypeFilter_ProcessesOnlyMatchingEvents()
    {
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var projectionStore = _serviceProvider.GetRequiredService<IProjectionStore>();
        var runner = _serviceProvider.GetRequiredService<ProjectionRunner>();
        var projection = new EventTypeFilteredProjection();

        var stream = new StreamIdentifier("TestOrder", "catchup-event-filtered");

        // Initial events
        await eventStore.AppendAsync(new StreamPointer(stream, 0),
        [
            new(new TestOrderCreatedEvent("order-1", 100m), null),
            new(new TestOrderUpdatedEvent("order-1", "Pending"), null)
        ]);

        // First catch-up
        var (firstView, _) = await runner.CatchUpAsync(projection);

        Assert.Equal(1, firstView.Count); // Only Created event

        // Add more events
        await eventStore.AppendAsync(new StreamPointer(stream, 2),
        [
            new(new TestOrderCreatedEvent("order-2", 200m), null),
            new(new TestOrderUpdatedEvent("order-2", "Confirmed"), null),
            new(new TestOrderCreatedEvent("order-3", 300m), null)
        ]);

        // Second catch-up
        var (secondView, _) = await runner.CatchUpAsync(
            projection,
            "EventTypeFilteredTest");

        Assert.Equal(3, secondView.Count); // 3 total Created events
    }

    [Fact]
    public async Task RebuildAsync_FilterMismatch_ThrowsInvalidOperationException()
    {
        // This test verifies that when a projection has filters but receives
        // an event that doesn't match, it throws an exception indicating a mismatch
        // between the filter configuration and the query.

        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var projection = new EventTypeFilteredProjection(); // Filtered to TestOrder.Created.v1

        var stream = new StreamIdentifier("TestOrder", "mismatch-test");
        await eventStore.AppendAsync(new StreamPointer(stream, 0),
        [
            new(new TestOrderCreatedEvent("order-1", 100m), null)
        ]);

        // Now load ALL events without filtering (simulating misconfiguration)
        var allEvents = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAllAsync(0, null, null))
        {
            allEvents.Add(evt);
        }

        // Manually apply events without using ProjectionRunner
        // (which would apply the filters correctly at the query level)
        var view = projection.InitialView();

        // The Created event should pass
        view = projection.Apply(view, allEvents[0]);
        Assert.Equal(1, view.Count);

        // Now add an Updated event that won't be filtered by the query
        await eventStore.AppendAsync(new StreamPointer(stream, 1),
        [
            new(new TestOrderUpdatedEvent("order-1", "Pending"), null)
        ]);

        // Load without event type filter (misconfiguration)
        var newEvents = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAllAsync(allEvents[^1].GlobalPosition, null, null))
        {
            newEvents.Add(evt);
        }

        // Applying the mismatched event should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(view, newEvents[0]));

        Assert.Contains("TestOrder.Updated.v1", ex.Message);
        Assert.Contains("TestOrder.Created.v1", ex.Message);
        Assert.Contains("filter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RebuildAsync_AggregateMismatch_ThrowsInvalidOperationException()
    {
        // Verifies that aggregate type mismatch throws when query doesn't match filter
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var projection = new OrderCounterProjection(); // Filtered to TestOrder

        var stream = new StreamIdentifier("OtherAggregate", "aggregate-mismatch");
        await eventStore.AppendAsync(new StreamPointer(stream, 0),
        [
            new(new OtherAggregateCreatedEvent("other-1", "data"), null)
        ]);

        // Load without aggregate filter (misconfiguration)
        var events = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAllAsync(0, null, null))
        {
            events.Add(evt);
        }

        // Applying the mismatched event should throw
        var view = projection.InitialView();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(view, events[0]));

        Assert.Contains("OtherAggregate", ex.Message);
        Assert.Contains("TestOrder", ex.Message);
        Assert.Contains("filter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CatchUpAsync_DifferentNamespaces_MaintainSeparateCheckpoints()
    {
        // Verifies that the same projection type in different namespaces
        // maintains completely separate checkpoint state
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var projectionStore = _serviceProvider.GetRequiredService<IProjectionStore>();
        var runner = _serviceProvider.GetRequiredService<ProjectionRunner>();
        var projection = new OrderCounterProjection();

        var stream = new StreamIdentifier("TestOrder", "namespace-test");

        // Create initial events
        await eventStore.AppendAsync(new StreamPointer(stream, 0),
        [
            new(new TestOrderCreatedEvent("order-1", 100m), null),
            new(new TestOrderUpdatedEvent("order-1", "Pending"), null)
        ]);

        // Catch up in "system" namespace
        var (systemView1, systemCheckpoint1) = await runner.CatchUpAsync(
            projection,
            "system");

        Assert.Equal(2, systemView1.Count);
        Assert.True(systemCheckpoint1 > 0);

        // Catch up in "testing" namespace - should start from zero, not from system checkpoint
        var (testingView1, testingCheckpoint1) = await runner.CatchUpAsync(
            projection,
            "testing");

        Assert.Equal(2, testingView1.Count); // Processes all events, not from system checkpoint
        Assert.Equal(systemCheckpoint1, testingCheckpoint1); // Same final position

        // Add more events
        await eventStore.AppendAsync(new StreamPointer(stream, 2),
        [
            new(new TestOrderCreatedEvent("order-2", 200m), null)
        ]);

        // Catch up system namespace - should process 1 new event
        var (systemView2, systemCheckpoint2) = await runner.CatchUpAsync(
            projection,
            "system");

        Assert.Equal(3, systemView2.Count); // 2 previous + 1 new
        Assert.True(systemCheckpoint2 > systemCheckpoint1);

        // Catch up testing namespace - should also process 1 new event
        var (testingView2, testingCheckpoint2) = await runner.CatchUpAsync(
            projection,
            "testing");

        Assert.Equal(3, testingView2.Count); // 2 previous + 1 new
        Assert.Equal(systemCheckpoint2, testingCheckpoint2); // Same final position

        // Verify both namespaces have separate stored checkpoints
        var systemStored = await projectionStore.LoadProjectionAsync<OrderCounterState>(
            "OrderCounter",
            "system");
        var testingStored = await projectionStore.LoadProjectionAsync<OrderCounterState>(
            "OrderCounter",
            "testing");

        Assert.NotNull(systemStored);
        Assert.NotNull(testingStored);
        Assert.Equal(3, systemStored.State.Count);
        Assert.Equal(3, testingStored.State.Count);
        Assert.Equal(systemCheckpoint2, systemStored.GlobalPosition);
        Assert.Equal(testingCheckpoint2, testingStored.GlobalPosition);
    }
}

[Projection("EventTypeFiltered", EventTypes = new[] { "TestOrder.Created.v1" })]
public class EventTypeFilteredProjection : Projection<OrderCounterState>
{
    public override OrderCounterState InitialView() => new() { Count = 0 };
    protected override OrderCounterState ApplyEvent(OrderCounterState view, StreamEvent streamEvent)
    {
        return new OrderCounterState { Count = view.Count + 1 };
    }
}

[Projection("BothFilters", AggregateTypes = new[] { "TestOrder" }, EventTypes = new[] { "TestOrder.Created.v1" })]
public class BothFiltersProjection : Projection<OrderCounterState>
{
    public override OrderCounterState InitialView() => new() { Count = 0 };
    protected override OrderCounterState ApplyEvent(OrderCounterState view, StreamEvent streamEvent)
    {
        return new OrderCounterState { Count = view.Count + 1 };
    }
}

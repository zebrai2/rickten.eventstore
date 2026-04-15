using Rickten.EventStore;
using Xunit;

namespace Rickten.Projector.Tests;

public class ProjectionRunnerTests
{
    [Fact]
    public async Task RebuildAsync_WithNoEvents_ReturnsInitialView()
    {
        var eventStore = new InMemoryEventStore();
        var projection = new TestProjection();

        var (view, lastVersion) = await ProjectionRunner.RebuildAsync(
            eventStore,
            projection,
            fromVersion: 0);

        Assert.Equal(0, view.Count);
        Assert.Equal(0, lastVersion);
    }

    [Fact]
    public async Task RebuildAsync_WithEvents_AppliesAllEvents()
    {
        var eventStore = new InMemoryEventStore();
        var streamId = new StreamIdentifier("Test", "1");

        await eventStore.AppendAsync(streamId, 0, new TestEvent { Value = 1 });
        await eventStore.AppendAsync(streamId, 1, new TestEvent { Value = 2 });
        await eventStore.AppendAsync(streamId, 2, new TestEvent { Value = 3 });

        var projection = new TestProjection();
        var (view, lastVersion) = await ProjectionRunner.RebuildAsync(
            eventStore,
            projection,
            fromVersion: 0);

        Assert.Equal(3, view.Count);
        Assert.Equal(6, view.Sum);
        Assert.Equal(3, lastVersion);
    }

    [Fact]
    public async Task RebuildAsync_WithFromVersion_SkipsPreviousEvents()
    {
        var eventStore = new InMemoryEventStore();
        var streamId = new StreamIdentifier("Test", "1");

        await eventStore.AppendAsync(streamId, 0, new TestEvent { Value = 1 });
        await eventStore.AppendAsync(streamId, 1, new TestEvent { Value = 2 });
        await eventStore.AppendAsync(streamId, 2, new TestEvent { Value = 3 });

        var projection = new TestProjection();
        var (view, lastVersion) = await ProjectionRunner.RebuildAsync(
            eventStore,
            projection,
            fromVersion: 2);

        Assert.Equal(1, view.Count);
        Assert.Equal(3, view.Sum);
        Assert.Equal(3, lastVersion);
    }

    [Fact]
    public async Task RebuildAsync_WithAggregateFilter_OnlyProcessesMatchingEvents()
    {
        var eventStore = new InMemoryEventStore();

        await eventStore.AppendAsync(new StreamIdentifier("Order", "1"), 0, new TestEvent { Value = 10 });
        await eventStore.AppendAsync(new StreamIdentifier("Product", "1"), 0, new TestEvent { Value = 20 });
        await eventStore.AppendAsync(new StreamIdentifier("Order", "2"), 0, new TestEvent { Value = 30 });

        var projection = new FilteredProjection();
        var (view, lastVersion) = await ProjectionRunner.RebuildAsync(
            eventStore,
            projection,
            fromVersion: 0);

        Assert.Equal(2, view.Count);
        Assert.Equal(40, view.Sum);
    }

    [Fact]
    public async Task CatchUpAsync_WithNoCheckpoint_BuildsFromStart()
    {
        var eventStore = new InMemoryEventStore();
        var projectionStore = new InMemoryProjectionStore();
        var streamId = new StreamIdentifier("Test", "1");

        await eventStore.AppendAsync(streamId, 0, new TestEvent { Value = 1 });
        await eventStore.AppendAsync(streamId, 1, new TestEvent { Value = 2 });

        var projection = new NamedProjection();
        var (view, version) = await ProjectionRunner.CatchUpAsync(
            eventStore,
            projectionStore,
            projection);

        Assert.Equal(2, view.Count);
        Assert.Equal(3, view.Sum);
        Assert.Equal(2, version);

        var saved = await projectionStore.LoadProjectionAsync<TestView>("TestProjection");
        Assert.NotNull(saved);
        Assert.Equal(2, saved.State.Count);
        Assert.Equal(2, saved.GlobalPosition);
    }

    [Fact]
    public async Task CatchUpAsync_WithExistingCheckpoint_ContinuesFromLastPosition()
    {
        var eventStore = new InMemoryEventStore();
        var projectionStore = new InMemoryProjectionStore();
        var streamId = new StreamIdentifier("Test", "1");

        await eventStore.AppendAsync(streamId, 0, new TestEvent { Value = 1 });
        await eventStore.AppendAsync(streamId, 1, new TestEvent { Value = 2 });

        await projectionStore.SaveProjectionAsync(
            "TestProjection",
            2,
            new TestView { Count = 2, Sum = 3 });

        await eventStore.AppendAsync(streamId, 2, new TestEvent { Value = 3 });

        var projection = new NamedProjection();
        var (view, version) = await ProjectionRunner.CatchUpAsync(
            eventStore,
            projectionStore,
            projection);

        Assert.Equal(3, view.Count);
        Assert.Equal(6, view.Sum);
        Assert.Equal(3, version);
    }

    [Fact]
    public async Task CatchUpAsync_WithNoNewEvents_ReturnsCurrent()
    {
        var eventStore = new InMemoryEventStore();
        var projectionStore = new InMemoryProjectionStore();
        var streamId = new StreamIdentifier("Test", "1");

        await eventStore.AppendAsync(streamId, 0, new TestEvent { Value = 1 });

        await projectionStore.SaveProjectionAsync(
            "TestProjection",
            1,
            new TestView { Count = 1, Sum = 1 });

        var projection = new NamedProjection();
        var (view, version) = await ProjectionRunner.CatchUpAsync(
            eventStore,
            projectionStore,
            projection);

        Assert.Equal(1, view.Count);
        Assert.Equal(1, view.Sum);
        Assert.Equal(1, version);
    }

    [Fact]
    public async Task CatchUpAsync_WithExplicitName_UsesProvidedName()
    {
        var eventStore = new InMemoryEventStore();
        var projectionStore = new InMemoryProjectionStore();
        var streamId = new StreamIdentifier("Test", "1");

        await eventStore.AppendAsync(streamId, 0, new TestEvent { Value = 1 });

        var projection = new NamedProjection();
        await ProjectionRunner.CatchUpAsync(
            eventStore,
            projectionStore,
            projection,
            projectionName: "CustomName");

        var saved = await projectionStore.LoadProjectionAsync<TestView>("CustomName");
        Assert.NotNull(saved);
        Assert.Equal(1, saved.State.Count);
    }

    [Fact]
    public async Task CatchUpAsync_WithoutName_ThrowsException()
    {
        var eventStore = new InMemoryEventStore();
        var projectionStore = new InMemoryProjectionStore();
        var projection = new TestProjection();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ProjectionRunner.CatchUpAsync(
                eventStore,
                projectionStore,
                projection));
    }

    private record TestView
    {
        public int Count { get; init; }
        public int Sum { get; init; }
    }

    private record TestEvent
    {
        public int Value { get; init; }
    }

    private class TestProjection : IProjection<TestView>
    {
        public TestView InitialView() => new TestView { Count = 0, Sum = 0 };

        public TestView Apply(TestView view, StreamEvent streamEvent)
        {
            if (streamEvent.Event is TestEvent e)
            {
                return new TestView
                {
                    Count = view.Count + 1,
                    Sum = view.Sum + e.Value
                };
            }
            return view;
        }
    }

    [Projection("TestProjection")]
    private class NamedProjection : Projection<TestView>
    {
        public override TestView InitialView() => new TestView { Count = 0, Sum = 0 };

        protected override TestView ApplyEvent(TestView view, StreamEvent streamEvent)
        {
            if (streamEvent.Event is TestEvent e)
            {
                return new TestView
                {
                    Count = view.Count + 1,
                    Sum = view.Sum + e.Value
                };
            }
            return view;
        }
    }

    [Projection("FilteredProjection", AggregateTypes = new[] { "Order" })]
    private class FilteredProjection : Projection<TestView>
    {
        public override TestView InitialView() => new TestView { Count = 0, Sum = 0 };

        protected override TestView ApplyEvent(TestView view, StreamEvent streamEvent)
        {
            if (streamEvent.Event is TestEvent e)
            {
                return new TestView
                {
                    Count = view.Count + 1,
                    Sum = view.Sum + e.Value
                };
            }
            return view;
        }
    }
}

using Rickten.EventStore;
using Xunit;

namespace Rickten.Projector.Tests;

public class ProjectionTests
{
    [Fact]
    public void Projection_WithoutAttribute_UsesClassName()
    {
        var projection = new UnattributedProjection();

        Assert.Equal("UnattributedProjection", projection.ProjectionName);
        Assert.Null(projection.AggregateTypeFilter);
        Assert.Null(projection.EventTypeFilter);
    }

    [Fact]
    public void Projection_WithNamedAttribute_UsesAttributeName()
    {
        var projection = new NamedAttributeProjection();

        Assert.Equal("CustomName", projection.ProjectionName);
    }

    [Fact]
    public void Projection_WithAggregateFilter_ExposesFilter()
    {
        var projection = new AggregateFilteredProjection();

        Assert.NotNull(projection.AggregateTypeFilter);
        Assert.Equal(2, projection.AggregateTypeFilter.Length);
        Assert.Contains("Order", projection.AggregateTypeFilter);
        Assert.Contains("Payment", projection.AggregateTypeFilter);
    }

    [Fact]
    public void Projection_WithEventTypeFilter_ExposesFilter()
    {
        var projection = new EventTypeFilteredProjection();

        Assert.NotNull(projection.EventTypeFilter);
        Assert.Equal(2, projection.EventTypeFilter.Length);
        Assert.Contains("OrderCreated", projection.EventTypeFilter);
        Assert.Contains("OrderPaid", projection.EventTypeFilter);
    }

    [Fact]
    public void Projection_WithBothFilters_ExposesBoth()
    {
        var projection = new FullyFilteredProjection();

        Assert.NotNull(projection.AggregateTypeFilter);
        Assert.Single(projection.AggregateTypeFilter);
        Assert.Contains("Order", projection.AggregateTypeFilter);

        Assert.NotNull(projection.EventTypeFilter);
        Assert.Single(projection.EventTypeFilter);
        Assert.Contains("OrderCreated", projection.EventTypeFilter);
    }

    [Fact]
    public void Projection_Apply_CallsApplyEventForMatchingEvents()
    {
        var projection = new CountingProjection();
        var streamId = new StreamIdentifier("Test", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            new TestEvent(),
            null);

        var view = projection.InitialView();
        view = projection.Apply(view, streamEvent);

        Assert.Equal(1, view);
    }

    [Fact]
    public void Projection_Apply_WithWrongAggregateType_ThrowsException()
    {
        var projection = new AggregateFilteredProjection();
        var streamId = new StreamIdentifier("Product", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            new TestEvent(),
            null);

        var view = projection.InitialView();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(view, streamEvent));

        Assert.Contains("Product", ex.Message);
        Assert.Contains("Order", ex.Message);
        Assert.Contains("Payment", ex.Message);
    }

    [Fact]
    public void ProjectionAttribute_StoresName()
    {
        var attr = new ProjectionAttribute("TestProjection");

        Assert.Equal("TestProjection", attr.Name);
        Assert.Null(attr.AggregateTypes);
        Assert.Null(attr.EventTypes);
        Assert.Null(attr.Description);
    }

    [Fact]
    public void ProjectionAttribute_WithAggregateTypes_StoresFilter()
    {
        var attr = new ProjectionAttribute("TestProjection")
        {
            AggregateTypes = new[] { "Order", "Payment" }
        };

        Assert.Equal(2, attr.AggregateTypes.Length);
        Assert.Contains("Order", attr.AggregateTypes);
        Assert.Contains("Payment", attr.AggregateTypes);
    }

    [Fact]
    public void ProjectionAttribute_WithEventTypes_StoresFilter()
    {
        var attr = new ProjectionAttribute("TestProjection")
        {
            EventTypes = new[] { "Created", "Updated" }
        };

        Assert.Equal(2, attr.EventTypes.Length);
        Assert.Contains("Created", attr.EventTypes);
        Assert.Contains("Updated", attr.EventTypes);
    }

    [Fact]
    public void ProjectionAttribute_WithDescription_StoresDescription()
    {
        var attr = new ProjectionAttribute("TestProjection")
        {
            Description = "A test projection"
        };

        Assert.Equal("A test projection", attr.Description);
    }

    private record TestEvent;

    private class UnattributedProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view;
    }

    [Projection("CustomName")]
    private class NamedAttributeProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view;
    }

    [Projection("Filtered", AggregateTypes = new[] { "Order", "Payment" })]
    private class AggregateFilteredProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view;
    }

    [Projection("Filtered", EventTypes = new[] { "OrderCreated", "OrderPaid" })]
    private class EventTypeFilteredProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view;
    }

    [Projection("FullyFiltered", AggregateTypes = new[] { "Order" }, EventTypes = new[] { "OrderCreated" })]
    private class FullyFilteredProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view;
    }

    private class CountingProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view + 1;
    }
}

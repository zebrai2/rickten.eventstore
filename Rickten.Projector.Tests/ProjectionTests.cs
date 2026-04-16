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
        var projection = new AttributeEventTypeFilteredProjection();

        Assert.NotNull(projection.EventTypeFilter);
        Assert.Equal(2, projection.EventTypeFilter.Length);
        Assert.Contains("OrderCreated", projection.EventTypeFilter);
        Assert.Contains("OrderPaid", projection.EventTypeFilter);
    }

    [Fact]
    public void Projection_WithBothFilters_ExposesBoth()
    {
        var projection = new AttributeFullyFilteredProjection();

        Assert.NotNull(projection.AggregateTypeFilter);
        Assert.Single(projection.AggregateTypeFilter);
        Assert.Contains("Order", projection.AggregateTypeFilter);

        Assert.NotNull(projection.EventTypeFilter);
        Assert.Single(projection.EventTypeFilter);
        Assert.Contains("OrderCreated", projection.EventTypeFilter);
    }

    [Fact]
    public void ProjectionAttribute_StoresFilters()
    {
        // Aggregate filter
        var attrWithAggregate = new ProjectionAttribute("Test")
        {
            AggregateTypes = new[] { "Order", "Payment" }
        };
        Assert.Equal(2, attrWithAggregate.AggregateTypes.Length);
        Assert.Contains("Order", attrWithAggregate.AggregateTypes);
        Assert.Contains("Payment", attrWithAggregate.AggregateTypes);

        // Event type filter
        var attrWithEvents = new ProjectionAttribute("Test")
        {
            EventTypes = new[] { "Created", "Updated" }
        };
        Assert.Equal(2, attrWithEvents.EventTypes.Length);
        Assert.Contains("Created", attrWithEvents.EventTypes);
        Assert.Contains("Updated", attrWithEvents.EventTypes);

        // Description
        var attrWithDescription = new ProjectionAttribute("Test")
        {
            Description = "A test projection"
        };
        Assert.Equal("A test projection", attrWithDescription.Description);
    }

    private record TestEvent;

    [Event("Order", "Created", 1)]
    private record OrderCreatedEvent(string OrderId, decimal Amount);

    [Event("Order", "Updated", 1)]
    private record OrderUpdatedEvent(string OrderId, string Status);

    private record UnattributedEvent;

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

    // Simple attribute tests - not testing actual filtering behavior, just attribute storage
    [Projection("Filtered", EventTypes = new[] { "OrderCreated", "OrderPaid" })]
    private class AttributeEventTypeFilteredProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view;
    }

    [Projection("FullyFiltered", AggregateTypes = new[] { "Order" }, EventTypes = new[] { "OrderCreated" })]
    private class AttributeFullyFilteredProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view;
    }

    private class CountingProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view + 1;
    }

    // Wire-name based filtering projections - test actual filtering contract
    [Projection("WireNameFiltered", EventTypes = new[] { "Order.Created.v1" })]
    private class WireNameFilteredProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view + 1;
    }

    [Projection("TypeNameFiltered", EventTypes = new[] { "UnattributedEvent" })]
    private class TypeNameFilteredProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view + 1;
    }

    [Projection("FullyFilteredWireName", AggregateTypes = new[] { "Order" }, EventTypes = new[] { "Order.Created.v1" })]
    private class FullyFilteredWireNameProjection : Projection<int>
    {
        public override int InitialView() => 0;
        protected override int ApplyEvent(int view, StreamEvent streamEvent) => view + 1;
    }
}


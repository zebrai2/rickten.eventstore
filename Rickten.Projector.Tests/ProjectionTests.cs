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
    public void Projection_Apply_WithEventTypeFilter_MatchingWireName_CallsApplyEvent()
    {
        // Test that events with matching wire names pass the filter
        var projection = new WireNameFilteredProjection();
        var streamId = new StreamIdentifier("Order", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            new OrderCreatedEvent("order-1", 100m),
            null);

        var view = projection.InitialView();
        view = projection.Apply(view, streamEvent);

        Assert.Equal(1, view); // Event was processed
    }

    [Fact]
    public void Projection_Apply_WithEventTypeFilter_WrongWireName_ThrowsException()
    {
        // Test that events with non-matching wire names trigger filter mismatch exception
        var projection = new WireNameFilteredProjection();
        var streamId = new StreamIdentifier("Order", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            new OrderUpdatedEvent("order-1", "Pending"),
            null);

        var view = projection.InitialView();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(view, streamEvent));

        Assert.Contains("Order.Updated.v1", ex.Message);
        Assert.Contains("Order.Created.v1", ex.Message);
        Assert.Contains("WireNameFiltered", ex.Message);
    }

    [Fact]
    public void Projection_Apply_WithEventTypeFilter_NoEventAttribute_UsesTypeName()
    {
        // Test that events without [Event] attribute fall back to Type.Name
        var projection = new TypeNameFilteredProjection();
        var streamId = new StreamIdentifier("Test", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            new UnattributedEvent(),
            null);

        var view = projection.InitialView();
        view = projection.Apply(view, streamEvent);

        Assert.Equal(1, view); // Event was processed using Type.Name
    }

    [Fact]
    public void Projection_Apply_WithEventTypeFilter_NoEventAttribute_WrongTypeName_ThrowsException()
    {
        // Test that unattributed events are validated against Type.Name
        var projection = new TypeNameFilteredProjection();
        var streamId = new StreamIdentifier("Test", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            new TestEvent(), // Type.Name = "TestEvent", not in filter
            null);

        var view = projection.InitialView();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(view, streamEvent));

        Assert.Contains("TestEvent", ex.Message);
        Assert.Contains("UnattributedEvent", ex.Message);
    }

    [Fact]
    public void Projection_Apply_WithBothFilters_MatchingBoth_CallsApplyEvent()
    {
        // Test that when both aggregate AND event type filters are set, both must match
        var projection = new FullyFilteredWireNameProjection();
        var streamId = new StreamIdentifier("Order", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            new OrderCreatedEvent("order-1", 100m),
            null);

        var view = projection.InitialView();
        view = projection.Apply(view, streamEvent);

        Assert.Equal(1, view); // Both filters passed
    }

    [Fact]
    public void Projection_Apply_WithBothFilters_WrongAggregate_ThrowsException()
    {
        // Test that aggregate type is checked first, even if event type matches
        var projection = new FullyFilteredWireNameProjection();
        var streamId = new StreamIdentifier("Payment", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            new OrderCreatedEvent("order-1", 100m), // Event matches, but aggregate doesn't
            null);

        var view = projection.InitialView();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(view, streamEvent));

        Assert.Contains("Payment", ex.Message);
        Assert.Contains("Order", ex.Message);
    }

    [Fact]
    public void Projection_Apply_WithBothFilters_WrongEventType_ThrowsException()
    {
        // Test that event type is checked after aggregate type
        var projection = new FullyFilteredWireNameProjection();
        var streamId = new StreamIdentifier("Order", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            new OrderUpdatedEvent("order-1", "Pending"), // Aggregate matches, but event doesn't
            null);

        var view = projection.InitialView();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(view, streamEvent));

        Assert.Contains("Order.Updated.v1", ex.Message);
        Assert.Contains("Order.Created.v1", ex.Message);
    }

    [Fact]
    public void Projection_Apply_WithEventTypeFilter_NullEvent_PassesFilter()
    {
        // Test that null events (e.g., metadata-only) skip event type filtering
        var projection = new WireNameFilteredProjection();
        var streamId = new StreamIdentifier("Order", "1");
        var streamEvent = new StreamEvent(
            new StreamPointer(streamId, 1),
            1,
            null, // Null event
            null);

        var view = projection.InitialView();
        view = projection.Apply(view, streamEvent);

        Assert.Equal(1, view); // Null events bypass event type filter
    }

    [Fact]
    public void Projection_Apply_NoFilters_AllEventsPass()
    {
        // Test that projections without filters process all events
        var projection = new CountingProjection();
        var streamId = new StreamIdentifier("AnyAggregate", "1");

        var view = projection.InitialView();

        // Different event types and aggregates
        view = projection.Apply(view, new StreamEvent(
            new StreamPointer(streamId, 1), 1, new TestEvent(), null));
        view = projection.Apply(view, new StreamEvent(
            new StreamPointer(new StreamIdentifier("Other", "2"), 1), 2, new UnattributedEvent(), null));
        view = projection.Apply(view, new StreamEvent(
            new StreamPointer(streamId, 2), 3, null, null));

        Assert.Equal(3, view); // All events processed
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


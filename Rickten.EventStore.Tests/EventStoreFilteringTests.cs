using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore;
using Rickten.EventStore.TypeMetadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Focused tests for EventStore.LoadAllAsync filtering behavior using actual wire names.
/// Tests verify that filtering uses the wire-name contract, not guessed CLR type names.
/// </summary>
public class EventStoreFilteringTests
{
    [Event("Product", "Created", 1)]
    public record ProductCreatedEvent(string Name, decimal Price);

    [Event("Product", "PriceChanged", 1)]
    public record ProductPriceChangedEvent(decimal NewPrice, decimal OldPrice);

    [Event("Product", "Discontinued", 2)]
    public record ProductDiscontinuedEventV2(string Reason, DateTime EffectiveDate);

    [Event("Customer", "Registered", 1)]
    public record CustomerRegisteredEvent(string Email, string Name);

    [Event("Customer", "AddressUpdated", 1)]
    public record CustomerAddressUpdatedEvent(string NewAddress);

    private static readonly ITypeMetadataRegistry Registry = TestTypeMetadataRegistry.Create();

    private EventStoreDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new EventStoreDbContext(options);
    }

    private EntityFramework.EventStore CreateStore(string dbName) => new EntityFramework.EventStore(CreateContext(dbName), Registry, new WireTypeSerializer(Registry));

    private StreamPointer MakePointer(string streamType, string streamId, long version) =>
        new StreamPointer(new StreamIdentifier(streamType, streamId), version);

    [Fact]
    public async Task LoadAllAsync_EventsFilter_UsesActualWireNames()
    {
        // This test verifies that eventsFilter uses the wire name from the registry,
        // not the CLR type name or a guessed name.
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        // Create events with known wire names
        var product1 = MakePointer("Product", "P1", 0);
        var product2 = MakePointer("Product", "P2", 0);
        var customer1 = MakePointer("Customer", "C1", 0);

        await store.AppendAsync(product1, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("Widget", 19.99m), null),
            new AppendEvent(new ProductPriceChangedEvent(24.99m, 19.99m), null)
        });

        await store.AppendAsync(product2, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("Gadget", 49.99m), null),
            new AppendEvent(new ProductDiscontinuedEventV2("End of life", DateTime.UtcNow), null)
        });

        await store.AppendAsync(customer1, new List<AppendEvent>
        {
            new AppendEvent(new CustomerRegisteredEvent("test@example.com", "Test User"), null)
        });

        // Filter by specific event type using exact wire name
        var createdEvents = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            eventsFilter: new[] { "Product.Created.v1" }))
        {
            createdEvents.Add(e);
        }

        // Should get exactly the 2 ProductCreatedEvent instances
        Assert.Equal(2, createdEvents.Count);
        Assert.All(createdEvents, e => Assert.IsType<ProductCreatedEvent>(e.Event));

        // Filter by multiple event types using wire names
        var multiFilter = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            eventsFilter: new[] { "Product.PriceChanged.v1", "Customer.Registered.v1" }))
        {
            multiFilter.Add(e);
        }

        Assert.Equal(2, multiFilter.Count);
        Assert.Contains(multiFilter, e => e.Event is ProductPriceChangedEvent);
        Assert.Contains(multiFilter, e => e.Event is CustomerRegisteredEvent);
    }

    [Fact]
    public async Task LoadAllAsync_EventsFilter_WithVersionNumber_FiltersCorrectly()
    {
        // Verify that version numbers in wire names are respected
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var product1 = MakePointer("Product", "P1", 0);
        await store.AppendAsync(product1, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("Test", 10m), null),
            new AppendEvent(new ProductDiscontinuedEventV2("Obsolete", DateTime.UtcNow), null)
        });

        // Filter specifically for v2 of discontinued event
        var v2Events = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            eventsFilter: new[] { "Product.Discontinued.v2" }))
        {
            v2Events.Add(e);
        }

        Assert.Single(v2Events);
        Assert.IsType<ProductDiscontinuedEventV2>(v2Events[0].Event);

        // Attempting to filter by v1 (which doesn't exist) should return nothing
        var v1Events = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            eventsFilter: new[] { "Product.Discontinued.v1" }))
        {
            v1Events.Add(e);
        }

        Assert.Empty(v1Events);
    }

    [Fact]
    public async Task LoadAllAsync_CombinedStreamAndEventFilters_AppliesBoth()
    {
        // Verify that both streamTypeFilter and eventsFilter work together (AND semantics)
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var product1 = MakePointer("Product", "P1", 0);
        var customer1 = MakePointer("Customer", "C1", 0);

        await store.AppendAsync(product1, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("Widget", 10m), null),
            new AppendEvent(new ProductPriceChangedEvent(15m, 10m), null)
        });

        await store.AppendAsync(customer1, new List<AppendEvent>
        {
            new AppendEvent(new CustomerRegisteredEvent("user@test.com", "User"), null)
        });

        // Filter: Product streams AND Created events only
        var filtered = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            streamTypeFilter: new[] { "Product" },
            eventsFilter: new[] { "Product.Created.v1" }))
        {
            filtered.Add(e);
        }

        // Should get only Product.Created, not PriceChanged or Customer events
        Assert.Single(filtered);
        Assert.IsType<ProductCreatedEvent>(filtered[0].Event);
        Assert.Equal("Product", filtered[0].StreamPointer.Stream.StreamType);
    }

    [Fact]
    public async Task LoadAllAsync_EventsFilter_WithNonExistentWireName_ReturnsEmpty()
    {
        // Verify graceful handling of wire names that don't exist
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var product1 = MakePointer("Product", "P1", 0);
        await store.AppendAsync(product1, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("Test", 10m), null)
        });

        // Filter by non-existent wire name
        var filtered = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            eventsFilter: new[] { "NonExistent.Event.v1" }))
        {
            filtered.Add(e);
        }

        // Should return empty, not throw
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task LoadAllAsync_EventsFilter_WithWrongCasing_DoesNotMatch()
    {
        // Wire names are case-sensitive; verify incorrect casing doesn't match
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var product1 = MakePointer("Product", "P1", 0);
        await store.AppendAsync(product1, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("Test", 10m), null)
        });

        // Try with incorrect casing
        var filtered = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            eventsFilter: new[] { "product.created.v1" })) // lowercase
        {
            filtered.Add(e);
        }

        // Should not match (wire names are case-sensitive)
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task LoadAllAsync_MultipleEventTypeFilters_ReturnsAllMatching()
    {
        // Verify OR semantics when multiple event types are specified
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var product1 = MakePointer("Product", "P1", 0);
        await store.AppendAsync(product1, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("A", 10m), null),
            new AppendEvent(new ProductPriceChangedEvent(15m, 10m), null),
            new AppendEvent(new ProductDiscontinuedEventV2("End", DateTime.UtcNow), null)
        });

        // Filter by 2 of the 3 event types
        var filtered = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            eventsFilter: new[] { "Product.Created.v1", "Product.Discontinued.v2" }))
        {
            filtered.Add(e);
        }

        // Should get 2 events (Created and Discontinued), not PriceChanged
        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, e => e.Event is ProductCreatedEvent);
        Assert.Contains(filtered, e => e.Event is ProductDiscontinuedEventV2);
        Assert.DoesNotContain(filtered, e => e.Event is ProductPriceChangedEvent);
    }

    [Fact]
    public async Task LoadAllAsync_NoFilters_ReturnsAllEventsInGlobalOrder()
    {
        // Baseline: verify that without filters, all events are returned in global position order
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var product1 = MakePointer("Product", "P1", 0);
        var customer1 = MakePointer("Customer", "C1", 0);
        var product2 = MakePointer("Product", "P2", 0);

        await store.AppendAsync(product1, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("First", 10m), null)
        });

        await store.AppendAsync(customer1, new List<AppendEvent>
        {
            new AppendEvent(new CustomerRegisteredEvent("user@test.com", "User"), null)
        });

        await store.AppendAsync(product2, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("Second", 20m), null)
        });

        // Load all without filters
        var allEvents = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync())
        {
            allEvents.Add(e);
        }

        // Should get all 3 events in global position order
        Assert.Equal(3, allEvents.Count);
        Assert.True(allEvents[0].GlobalPosition < allEvents[1].GlobalPosition);
        Assert.True(allEvents[1].GlobalPosition < allEvents[2].GlobalPosition);

        // Verify the order matches insertion order
        Assert.IsType<ProductCreatedEvent>(allEvents[0].Event);
        Assert.IsType<CustomerRegisteredEvent>(allEvents[1].Event);
        Assert.IsType<ProductCreatedEvent>(allEvents[2].Event);
    }

    [Fact]
    public async Task LoadAllAsync_WithCheckpointAndEventFilter_UsesExclusiveSemantics()
    {
        // Verify that checkpoint + event filter work together with exclusive semantics
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var product1 = MakePointer("Product", "P1", 0);
        await store.AppendAsync(product1, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("A", 10m), null),
            new AppendEvent(new ProductPriceChangedEvent(15m, 10m), null),
            new AppendEvent(new ProductCreatedEvent("B", 20m), null),
            new AppendEvent(new ProductPriceChangedEvent(25m, 20m), null)
        });

        // Load all ProductCreated events to get their positions
        var allCreated = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            eventsFilter: new[] { "Product.Created.v1" }))
        {
            allCreated.Add(e);
        }

        Assert.Equal(2, allCreated.Count);

        // Get checkpoint after first Created event
        var checkpoint = allCreated[0].GlobalPosition;

        // Resume from checkpoint with same filter
        var afterCheckpoint = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            fromGlobalPosition: checkpoint,
            eventsFilter: new[] { "Product.Created.v1" }))
        {
            afterCheckpoint.Add(e);
        }

        // Should get only the second Created event (exclusive semantics)
        Assert.Single(afterCheckpoint);
        Assert.True(afterCheckpoint[0].GlobalPosition > checkpoint);
        Assert.IsType<ProductCreatedEvent>(afterCheckpoint[0].Event);
    }

    [Fact]
    public async Task LoadAllAsync_EmptyFilters_TreatedAsNoFilter()
    {
        // Verify that empty filter arrays are treated the same as null (no filtering)
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var product1 = MakePointer("Product", "P1", 0);
        await store.AppendAsync(product1, new List<AppendEvent>
        {
            new AppendEvent(new ProductCreatedEvent("Test", 10m), null)
        });

        // Load with empty filters
        var withEmptyFilters = new List<StreamEvent>();
        await foreach (var e in store.LoadAllAsync(
            streamTypeFilter: Array.Empty<string>(),
            eventsFilter: Array.Empty<string>()))
        {
            withEmptyFilters.Add(e);
        }

        // Should get the event (empty filters = no filtering)
        Assert.Single(withEmptyFilters);
    }
}

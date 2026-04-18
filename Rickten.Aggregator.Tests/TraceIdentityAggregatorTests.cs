using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.EventStore.EntityFramework;
using Xunit;

namespace Rickten.Aggregator.Tests;

/// <summary>
/// Tests for trace identity metadata propagation through StateRunner.
/// </summary>
public class TraceIdentityAggregatorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventStoreDbContext> _options;

    public TraceIdentityAggregatorTests()
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

    private EventStore.EntityFramework.EventStore CreateEventStore()
    {
        var registry = TestTypeMetadataRegistry.Create();
        return new EventStore.EntityFramework.EventStore(
            new EventStoreDbContext(_options),
            registry,
            new EventStore.EntityFramework.Serialization.WireTypeSerializer(registry));
    }

    [Event("TraceProduct", "Created", 1)]
    public record ProductCreated(string Name, decimal Price);

    [Event("TraceProduct", "PriceChanged", 1)]
    public record PriceChanged(decimal NewPrice);

    [Aggregate("TraceProduct")]
    public record ProductState(string Name, decimal Price);

    [Command("TraceProduct")]
    public record CreateProduct(string Name, decimal Price);

    [Command("TraceProduct")]
    public record ChangePrice(decimal NewPrice);

    public class ProductFolder : StateFolder<ProductState>
    {
        public ProductFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

        public override ProductState InitialState() => new ProductState("", 0);

        protected ProductState When(ProductCreated e, ProductState state)
        {
            return state with { Name = e.Name, Price = e.Price };
        }

        protected ProductState When(PriceChanged e, ProductState state)
        {
            return state with { Price = e.NewPrice };
        }
    }

    public class CreateProductDecider : CommandDecider<ProductState, CreateProduct>
    {
        protected override IReadOnlyList<object> ExecuteCommand(ProductState state, CreateProduct command)
        {
            return Event(new ProductCreated(command.Name, command.Price));
        }
    }

    public class ChangePriceDecider : CommandDecider<ProductState, ChangePrice>
    {
        protected override IReadOnlyList<object> ExecuteCommand(ProductState state, ChangePrice command)
        {
            return Event(new PriceChanged(command.NewPrice));
        }
    }

    [Fact]
    public async Task ExecuteAsync_Preserves_Provided_CorrelationId()
    {
        var eventStore = CreateEventStore();
        var registry = TestTypeMetadataRegistry.Create();
        var folder = new ProductFolder(registry);
        var decider = new CreateProductDecider();
        var streamId = new StreamIdentifier("TraceProduct", "p1");
        var stateRunner = new StateRunner(eventStore);

        var providedCorrelationId = Guid.NewGuid();
        var metadata = new List<AppendMetadata>
        {
            new AppendMetadata(EventMetadataKeys.CorrelationId, providedCorrelationId)
        };

        var result = await stateRunner.ExecuteAsync(
            folder,
            decider,
            streamId,
            new CreateProduct("Widget", 10m),
            registry,
            metadata: metadata);

        Assert.Single(result.Events);
        var correlationId = result.Events[0].Metadata.GetCorrelationId();
        Assert.Equal(providedCorrelationId, correlationId);
    }

    [Fact]
    public async Task ExecuteAsync_Preserves_Provided_CausationId()
    {
        var eventStore = CreateEventStore();
        var registry = TestTypeMetadataRegistry.Create();
        var folder = new ProductFolder(registry);
        var decider = new CreateProductDecider();
        var streamId = new StreamIdentifier("TraceProduct", "p2");
        var stateRunner = new StateRunner(eventStore);

        var providedCausationId = Guid.NewGuid();
        var metadata = new List<AppendMetadata>
        {
            new AppendMetadata(EventMetadataKeys.CausationId, providedCausationId)
        };

        var result = await stateRunner.ExecuteAsync(
            folder,
            decider,
            streamId,
            new CreateProduct("Widget", 10m),
            registry,
            metadata: metadata);

        Assert.Single(result.Events);
        var causationId = result.Events[0].Metadata.GetCausationId();
        Assert.Equal(providedCausationId, causationId);
    }

    [Fact]
    public async Task ExecuteAsync_Multiple_Events_Share_BatchId()
    {
        var eventStore = CreateEventStore();
        var registry = TestTypeMetadataRegistry.Create();
        var folder = new ProductFolder(registry);

        // Create a decider that produces multiple events
        var multiEventDecider = new MultiEventDecider();
        var streamId = new StreamIdentifier("TraceProduct", "p3");
        var stateRunner = new StateRunner(eventStore);

        var result = await stateRunner.ExecuteAsync(
            folder,
            multiEventDecider,
            streamId,
            new CreateProduct("Widget", 10m),
            registry);

        Assert.Equal(2, result.Events.Count);

        var batchId1 = result.Events[0].Metadata.GetBatchId();
        var batchId2 = result.Events[1].Metadata.GetBatchId();

        Assert.NotNull(batchId1);
        Assert.Equal(batchId1, batchId2);
    }

    [Fact]
    public async Task ExecuteAsync_Multiple_Events_Have_Distinct_EventIds()
    {
        var eventStore = CreateEventStore();
        var registry = TestTypeMetadataRegistry.Create();
        var folder = new ProductFolder(registry);

        var multiEventDecider = new MultiEventDecider();
        var streamId = new StreamIdentifier("TraceProduct", "p4");
        var stateRunner = new StateRunner(eventStore);

        var result = await stateRunner.ExecuteAsync(
            folder,
            multiEventDecider,
            streamId,
            new CreateProduct("Widget", 10m),
            registry);

        Assert.Equal(2, result.Events.Count);

        var eventId1 = result.Events[0].Metadata.GetEventId();
        var eventId2 = result.Events[1].Metadata.GetEventId();

        Assert.NotNull(eventId1);
        Assert.NotNull(eventId2);
        Assert.NotEqual(eventId1, eventId2);
    }

    [Fact]
    public async Task ExecuteAsync_Different_Command_Executions_Have_Different_BatchIds()
    {
        var eventStore = CreateEventStore();
        var registry = TestTypeMetadataRegistry.Create();
        var folder = new ProductFolder(registry);
        var decider = new CreateProductDecider();
        var streamId = new StreamIdentifier("TraceProduct", "p5");
        var stateRunner = new StateRunner(eventStore);

        // First command execution
        var result1 = await stateRunner.ExecuteAsync(
            folder,
            decider,
            streamId,
            new CreateProduct("Widget", 10m),
            registry);

        // Second command execution
        var changePriceDecider = new ChangePriceDecider();
        var result2 = await stateRunner.ExecuteAsync(
            folder,
            changePriceDecider,
            streamId,
            new ChangePrice(15m),
            registry);

        var batchId1 = result1.Events[0].Metadata.GetBatchId();
        var batchId2 = result2.Events[0].Metadata.GetBatchId();

        Assert.NotNull(batchId1);
        Assert.NotNull(batchId2);
        Assert.NotEqual(batchId1, batchId2);
    }

    private class MultiEventDecider : CommandDecider<ProductState, CreateProduct>
    {
        protected override IReadOnlyList<object> ExecuteCommand(ProductState state, CreateProduct command)
        {
            return Events(
                new ProductCreated(command.Name, command.Price),
                new PriceChanged(command.Price)
            );
        }
    }
}

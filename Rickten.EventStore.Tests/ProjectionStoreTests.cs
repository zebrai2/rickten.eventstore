using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore.TypeMetadata;
using Rickten.EventStore.Tests;
using Rickten.Projector;
using System;
using System.Threading.Tasks;

public class ProjectionStoreTests
{
    private EventStoreDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new EventStoreDbContext(options);
    }

    private ProjectionStore CreateStore(string dbName)
    {
        var registry = TestTypeMetadataRegistry.Create();
        return new ProjectionStore(CreateContext(dbName), new WireTypeSerializer(registry));
    }

    [Fact]
    public async Task SaveAndLoadProjection_Works()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var key = "OrderSummary";
        var state = new OrderSummaryState { Count = 5 };

        await store.SaveProjectionAsync(key, 10, state);
        var loaded = await store.LoadProjectionAsync<OrderSummaryState>(key);

        Assert.NotNull(loaded);
        Assert.Equal(10, loaded.GlobalPosition);
        Assert.Equal(5, loaded.State.Count);
    }

    [Fact]
    public async Task LoadProjectionAsync_ReturnsNullIfNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var loaded = await store.LoadProjectionAsync<OrderSummaryState>("notfound");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveProjectionAsync_UpdatesExisting()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var key = "OrderSummary2";

        await store.SaveProjectionAsync(key, 1, new OrderSummaryState { Count = 1 });
        await store.SaveProjectionAsync(key, 2, new OrderSummaryState { Count = 2 });

        var loaded = await store.LoadProjectionAsync<OrderSummaryState>(key);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.GlobalPosition);
        Assert.Equal(2, loaded.State.Count);
    }

    [Fact]
    public async Task LoadProjectionAsync_ThrowsWhenTypeDoesNotMatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var key = "ProductSummary";

        // Save as ProductSummaryState
        await store.SaveProjectionAsync(key, 1, new ProductSummaryState { Total = 100 });

        // Try to load as OrderSummaryState - should throw
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.LoadProjectionAsync<OrderSummaryState>(key));

        Assert.Contains("type mismatch", ex.Message);
        Assert.Contains("ProductSummary", ex.Message);
    }

    [Fact]
    public async Task SaveProjectionAsync_StoresWireType()
    {
        var dbName = Guid.NewGuid().ToString();
        var context = CreateContext(dbName);
        var registry = TestTypeMetadataRegistry.Create();
        var store = new ProjectionStore(context, new WireTypeSerializer(registry));
        var key = "OrderSummary3";

        await store.SaveProjectionAsync(key, 1, new OrderSummaryState { Count = 5 });

        // Verify the wire type was stored
        var entity = await context.Projections.FirstOrDefaultAsync(p => p.ProjectionKey == key);
        Assert.NotNull(entity);
        Assert.Equal("Projection.OrderSummary.OrderSummaryState", entity.StateType);
    }

    [Fact]
    public async Task SaveProjectionAsync_IgnoresStaleSave_PreservesNewerCheckpoint()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var key = "OrderSummary4";

        // Save at position 200
        await store.SaveProjectionAsync(key, 200, new OrderSummaryState { Count = 200 });

        // Try to save at older position 150 - should be ignored
        await store.SaveProjectionAsync(key, 150, new OrderSummaryState { Count = 150 });

        // Verify position 200 is still intact
        var loaded = await store.LoadProjectionAsync<OrderSummaryState>(key);
        Assert.NotNull(loaded);
        Assert.Equal(200, loaded.GlobalPosition);
        Assert.Equal(200, loaded.State.Count);
    }

    [Fact]
    public async Task SaveProjectionAsync_SamePosition_UpdatesState()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var key = "OrderSummary5";

        // Save at position 200
        await store.SaveProjectionAsync(key, 200, new OrderSummaryState { Count = 200 });

        // Save again at same position 200 with different state - should update
        await store.SaveProjectionAsync(key, 200, new OrderSummaryState { Count = 999 });

        // Verify state was updated
        var loaded = await store.LoadProjectionAsync<OrderSummaryState>(key);
        Assert.NotNull(loaded);
        Assert.Equal(200, loaded.GlobalPosition);
        Assert.Equal(999, loaded.State.Count);
    }

    [Projection("OrderSummary")]
    private class OrderSummaryState
    {
        public int Count { get; set; }
    }

    [Projection("ProductSummary")]
    private class ProductSummaryState
    {
        public int Total { get; set; }
    }
}

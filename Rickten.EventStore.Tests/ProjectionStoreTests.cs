using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.TypeMetadata;
using Rickten.EventStore.Tests;
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
        return new ProjectionStore(CreateContext(dbName), registry);
    }

    [Fact]
    public async Task SaveAndLoadProjection_Works()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var key = "OrderSummary";
        await store.SaveProjectionAsync(key, 10, new { Count = 5 });
        var loaded = await store.LoadProjectionAsync<dynamic>(key);
        Assert.NotNull(loaded);
        Assert.Equal(10, loaded.GlobalPosition);
        Assert.Equal(5, (int)loaded.State.count);
    }

    [Fact]
    public async Task LoadProjectionAsync_ReturnsNullIfNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var loaded = await store.LoadProjectionAsync<dynamic>("notfound");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveProjectionAsync_UpdatesExisting()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var key = "OrderSummary2";
        await store.SaveProjectionAsync(key, 1, new { Count = 1 });
        await store.SaveProjectionAsync(key, 2, new { Count = 2 });
        var loaded = await store.LoadProjectionAsync<dynamic>(key);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.GlobalPosition);
        Assert.Equal(2, (int)loaded.State.count);
    }
}

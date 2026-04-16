using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using Rickten.EventStore.TypeMetadata;
using Rickten.Aggregator;
using System;
using System.Threading.Tasks;
using Rickten.EventStore.Tests;

[Aggregate("Order")]
public record OrderState(string Status);

public class SnapshotStoreTests
{
    private static readonly ITypeMetadataRegistry Registry = TestTypeMetadataRegistry.Create();

    private EventStoreDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new EventStoreDbContext(options);
    }

    private SnapshotStore CreateStore(string dbName) => new SnapshotStore(CreateContext(dbName), Registry);

    [Fact]
    public async Task SaveAndLoadSnapshot_Works()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = new StreamPointer(new StreamIdentifier("Order", "1"), 2);
        var state = new OrderState("shipped");
        await store.SaveSnapshotAsync(pointer, state);
        var loaded = await store.LoadSnapshotAsync(pointer.Stream);
        Assert.NotNull(loaded);
        Assert.Equal(pointer.Version, loaded.StreamPointer.Version);

        // Verify payload correctness, not just version
        var loadedState = Assert.IsType<OrderState>(loaded.State);
        Assert.Equal("shipped", loadedState.Status);
    }

    [Fact]
    public async Task LoadSnapshotAsync_ReturnsNullIfNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var loaded = await store.LoadSnapshotAsync(new StreamIdentifier("Order", "notfound"));
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveSnapshotAsync_UpdatesExisting()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = new StreamPointer(new StreamIdentifier("Order", "2"), 1);
        await store.SaveSnapshotAsync(pointer, new OrderState("pending"));
        pointer = new StreamPointer(pointer.Stream, 2);
        await store.SaveSnapshotAsync(pointer, new OrderState("complete"));
        var loaded = await store.LoadSnapshotAsync(pointer.Stream);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.StreamPointer.Version);

        // Verify payload was updated, not just version
        var loadedState = Assert.IsType<OrderState>(loaded.State);
        Assert.Equal("complete", loadedState.Status);
    }
}


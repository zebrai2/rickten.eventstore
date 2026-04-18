using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.EntityFramework.Serialization;
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

    private SnapshotStore CreateStore(string dbName) => new SnapshotStore(CreateContext(dbName), new WireTypeSerializer(Registry));

    [Fact]
    public async Task SaveAndLoadSnapshot_VerifiesVersionAndPayload()
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
    public async Task SaveSnapshotAsync_UpdatesExistingVersionAndPayload()
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

    [Fact]
    public async Task SaveSnapshotAsync_IgnoresStaleSnapshot_PreservesNewerVersion()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var stream = new StreamIdentifier("Order", "3");

        // Save at version 10
        var pointer1 = new StreamPointer(stream, 10);
        await store.SaveSnapshotAsync(pointer1, new OrderState("version10"));

        // Try to save at older version 5 - should be ignored
        var pointer2 = new StreamPointer(stream, 5);
        await store.SaveSnapshotAsync(pointer2, new OrderState("version5"));

        // Verify version 10 is still intact
        var loaded = await store.LoadSnapshotAsync(stream);
        Assert.NotNull(loaded);
        Assert.Equal(10, loaded.StreamPointer.Version);
        var loadedState = Assert.IsType<OrderState>(loaded.State);
        Assert.Equal("version10", loadedState.Status);
    }

    [Fact]
    public async Task SaveSnapshotAsync_SameVersion_UpdatesState()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var stream = new StreamIdentifier("Order", "4");

        // Save at version 10
        var pointer = new StreamPointer(stream, 10);
        await store.SaveSnapshotAsync(pointer, new OrderState("first"));

        // Save again at same version 10 with different state - should update
        await store.SaveSnapshotAsync(pointer, new OrderState("second"));

        // Verify state was updated
        var loaded = await store.LoadSnapshotAsync(stream);
        Assert.NotNull(loaded);
        Assert.Equal(10, loaded.StreamPointer.Version);
        var loadedState = Assert.IsType<OrderState>(loaded.State);
        Assert.Equal("second", loadedState.Status);
    }
}


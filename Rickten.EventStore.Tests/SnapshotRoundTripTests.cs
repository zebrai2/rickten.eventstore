using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using Rickten.EventStore.TypeMetadata;
using Rickten.Aggregator;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Focused tests for snapshot round-trip correctness.
/// Verifies that snapshots correctly persist and restore both metadata (version, pointer)
/// AND payload data (state contents), using the wire-name contract.
/// </summary>
public class SnapshotRoundTripTests
{
    [Aggregate("ShoppingCart")]
    public record ShoppingCartState(
        string CustomerId,
        decimal TotalAmount,
        int ItemCount,
        DateTime CreatedAt,
        bool IsCheckedOut);

    [Aggregate("UserProfile")]
    public record UserProfileState(
        string Email,
        string FullName,
        DateTime LastLoginAt,
        int LoginCount);

    [Aggregate("Inventory")]
    public record InventoryState(
        int CurrentStock,
        int ReservedStock,
        decimal UnitPrice,
        DateTime LastRestocked);

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
    public async Task SaveAndLoad_VerifiesPayloadDataRoundTrip()
    {
        // This test verifies that ALL state properties round-trip correctly, not just version
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var pointer = new StreamPointer(new StreamIdentifier("ShoppingCart", "cart-123"), 5);
        var originalState = new ShoppingCartState(
            CustomerId: "customer-456",
            TotalAmount: 149.99m,
            ItemCount: 3,
            CreatedAt: new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            IsCheckedOut: false);

        await store.SaveSnapshotAsync(pointer, originalState);

        // Load and verify ALL properties match
        var loaded = await store.LoadSnapshotAsync(pointer.Stream);

        Assert.NotNull(loaded);
        Assert.Equal(pointer.Version, loaded.StreamPointer.Version);
        Assert.Equal(pointer.Stream.StreamType, loaded.StreamPointer.Stream.StreamType);
        Assert.Equal(pointer.Stream.Identifier, loaded.StreamPointer.Stream.Identifier);

        // Verify payload correctness
        var loadedState = Assert.IsType<ShoppingCartState>(loaded.State);
        Assert.Equal(originalState.CustomerId, loadedState.CustomerId);
        Assert.Equal(originalState.TotalAmount, loadedState.TotalAmount);
        Assert.Equal(originalState.ItemCount, loadedState.ItemCount);
        Assert.Equal(originalState.CreatedAt, loadedState.CreatedAt);
        Assert.Equal(originalState.IsCheckedOut, loadedState.IsCheckedOut);
    }

    [Fact]
    public async Task SaveAndLoad_VerifiesStateTypeWireName()
    {
        // Verify that the snapshot stores and uses the wire name for StateType
        var dbName = Guid.NewGuid().ToString();
        var context = CreateContext(dbName);
        var store = new SnapshotStore(context, Registry);

        var pointer = new StreamPointer(new StreamIdentifier("ShoppingCart", "cart-789"), 3);
        var state = new ShoppingCartState("cust-1", 50m, 2, DateTime.UtcNow, false);

        await store.SaveSnapshotAsync(pointer, state);

        // Query the database directly to verify wire name is stored
        var entity = await context.Snapshots
            .FirstAsync(s => s.StreamType == "ShoppingCart" && s.StreamIdentifier == "cart-789");

        // Verify StateType uses wire name, not CLR type name
        Assert.Equal("ShoppingCart.ShoppingCartState", entity.StateType);
        Assert.NotEqual(typeof(ShoppingCartState).FullName, entity.StateType);
    }

    [Fact]
    public async Task SaveAndLoad_ComplexStateWithDecimalsAndDates_RoundTripsCorrectly()
    {
        // Edge case: verify precision for decimals and DateTime values
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var pointer = new StreamPointer(new StreamIdentifier("Inventory", "item-555"), 10);
        var originalState = new InventoryState(
            CurrentStock: 1234,
            ReservedStock: 567,
            UnitPrice: 19.99m, // decimal precision
            LastRestocked: new DateTime(2024, 3, 15, 14, 22, 33, DateTimeKind.Utc));

        await store.SaveSnapshotAsync(pointer, originalState);
        var loaded = await store.LoadSnapshotAsync(pointer.Stream);

        Assert.NotNull(loaded);
        var loadedState = Assert.IsType<InventoryState>(loaded.State);

        // Verify exact values including precision
        Assert.Equal(originalState.CurrentStock, loadedState.CurrentStock);
        Assert.Equal(originalState.ReservedStock, loadedState.ReservedStock);
        Assert.Equal(originalState.UnitPrice, loadedState.UnitPrice);
        Assert.Equal(originalState.LastRestocked, loadedState.LastRestocked);
    }

    [Fact]
    public async Task SaveSnapshot_UpdatesExistingWithNewPayload()
    {
        // Verify that updating a snapshot changes both version AND payload
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var streamId = new StreamIdentifier("UserProfile", "user-111");
        var pointer1 = new StreamPointer(streamId, 1);
        var state1 = new UserProfileState("user@example.com", "John Doe", DateTime.UtcNow, 5);

        await store.SaveSnapshotAsync(pointer1, state1);

        // Update with new version and different payload
        var pointer2 = new StreamPointer(streamId, 10);
        var state2 = new UserProfileState("updated@example.com", "Jane Smith", DateTime.UtcNow.AddDays(1), 20);

        await store.SaveSnapshotAsync(pointer2, state2);

        // Load and verify it has the NEW data, not the old
        var loaded = await store.LoadSnapshotAsync(streamId);

        Assert.NotNull(loaded);
        Assert.Equal(10, loaded.StreamPointer.Version);

        var loadedState = Assert.IsType<UserProfileState>(loaded.State);
        Assert.Equal("updated@example.com", loadedState.Email);
        Assert.Equal("Jane Smith", loadedState.FullName);
        Assert.Equal(20, loadedState.LoginCount);

        // Verify old values are gone
        Assert.NotEqual(state1.Email, loadedState.Email);
        Assert.NotEqual(state1.FullName, loadedState.FullName);
    }

    [Fact]
    public async Task SaveSnapshot_MultipleDifferentStreams_EachHasOwnSnapshot()
    {
        // Verify snapshots for different streams are isolated
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var cart1 = new StreamIdentifier("ShoppingCart", "cart-A");
        var cart2 = new StreamIdentifier("ShoppingCart", "cart-B");
        var profile1 = new StreamIdentifier("UserProfile", "user-X");

        var cartState1 = new ShoppingCartState("cust-1", 100m, 5, DateTime.UtcNow, false);
        var cartState2 = new ShoppingCartState("cust-2", 200m, 10, DateTime.UtcNow, true);
        var profileState1 = new UserProfileState("x@example.com", "User X", DateTime.UtcNow, 3);

        await store.SaveSnapshotAsync(new StreamPointer(cart1, 3), cartState1);
        await store.SaveSnapshotAsync(new StreamPointer(cart2, 5), cartState2);
        await store.SaveSnapshotAsync(new StreamPointer(profile1, 2), profileState1);

        // Load each snapshot and verify isolation
        var loadedCart1 = await store.LoadSnapshotAsync(cart1);
        var loadedCart2 = await store.LoadSnapshotAsync(cart2);
        var loadedProfile1 = await store.LoadSnapshotAsync(profile1);

        Assert.NotNull(loadedCart1);
        Assert.NotNull(loadedCart2);
        Assert.NotNull(loadedProfile1);

        var cart1State = Assert.IsType<ShoppingCartState>(loadedCart1.State);
        var cart2State = Assert.IsType<ShoppingCartState>(loadedCart2.State);
        var profile1State = Assert.IsType<UserProfileState>(loadedProfile1.State);

        Assert.Equal("cust-1", cart1State.CustomerId);
        Assert.Equal("cust-2", cart2State.CustomerId);
        Assert.Equal("x@example.com", profile1State.Email);

        Assert.Equal(3, loadedCart1.StreamPointer.Version);
        Assert.Equal(5, loadedCart2.StreamPointer.Version);
        Assert.Equal(2, loadedProfile1.StreamPointer.Version);
    }

    [Fact]
    public async Task SaveSnapshot_WithZeroVersion_Works()
    {
        // Edge case: snapshot at version 0 (before any events)
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var pointer = new StreamPointer(new StreamIdentifier("ShoppingCart", "empty-cart"), 0);
        var state = new ShoppingCartState("guest", 0m, 0, DateTime.UtcNow, false);

        await store.SaveSnapshotAsync(pointer, state);

        var loaded = await store.LoadSnapshotAsync(pointer.Stream);

        Assert.NotNull(loaded);
        Assert.Equal(0, loaded.StreamPointer.Version);
        var loadedState = Assert.IsType<ShoppingCartState>(loaded.State);
        Assert.Equal("guest", loadedState.CustomerId);
    }

    [Fact]
    public async Task LoadSnapshot_ForNonExistentStream_ReturnsNull()
    {
        // Verify graceful handling of missing snapshots
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var loaded = await store.LoadSnapshotAsync(new StreamIdentifier("NonExistent", "no-such-stream"));

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveSnapshot_SameStreamMultipleTimes_OnlyKeepsLatest()
    {
        // Verify that multiple saves to the same stream result in a single snapshot (latest)
        var dbName = Guid.NewGuid().ToString();
        var context = CreateContext(dbName);
        var store = new SnapshotStore(context, Registry);

        var streamId = new StreamIdentifier("ShoppingCart", "cart-update-test");

        await store.SaveSnapshotAsync(new StreamPointer(streamId, 1), new ShoppingCartState("v1", 10m, 1, DateTime.UtcNow, false));
        await store.SaveSnapshotAsync(new StreamPointer(streamId, 2), new ShoppingCartState("v2", 20m, 2, DateTime.UtcNow, false));
        await store.SaveSnapshotAsync(new StreamPointer(streamId, 3), new ShoppingCartState("v3", 30m, 3, DateTime.UtcNow, false));

        // Verify only 1 snapshot exists in DB
        var count = await context.Snapshots
            .CountAsync(s => s.StreamType == "ShoppingCart" && s.StreamIdentifier == "cart-update-test");

        Assert.Equal(1, count);

        // Verify it's the latest
        var loaded = await store.LoadSnapshotAsync(streamId);
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.StreamPointer.Version);
        var state = Assert.IsType<ShoppingCartState>(loaded.State);
        Assert.Equal("v3", state.CustomerId);
    }

    [Fact]
    public async Task SaveSnapshot_StateWithBooleanFalse_DeserializesCorrectly()
    {
        // Edge case: verify boolean false values are persisted (not treated as default/missing)
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var pointer = new StreamPointer(new StreamIdentifier("ShoppingCart", "bool-test"), 1);
        var state = new ShoppingCartState("test", 50m, 2, DateTime.UtcNow, false);

        await store.SaveSnapshotAsync(pointer, state);
        var loaded = await store.LoadSnapshotAsync(pointer.Stream);

        Assert.NotNull(loaded);
        var loadedState = Assert.IsType<ShoppingCartState>(loaded.State);
        Assert.False(loadedState.IsCheckedOut); // Explicitly verify false, not default
    }

    [Fact]
    public async Task SaveSnapshot_StateWithBooleanTrue_DeserializesCorrectly()
    {
        // Mirror test: verify boolean true is also persisted
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var pointer = new StreamPointer(new StreamIdentifier("ShoppingCart", "bool-test-true"), 1);
        var state = new ShoppingCartState("test", 50m, 2, DateTime.UtcNow, true);

        await store.SaveSnapshotAsync(pointer, state);
        var loaded = await store.LoadSnapshotAsync(pointer.Stream);

        Assert.NotNull(loaded);
        var loadedState = Assert.IsType<ShoppingCartState>(loaded.State);
        Assert.True(loadedState.IsCheckedOut);
    }

    [Fact]
    public async Task SaveSnapshot_LargeDecimalValues_MaintainsPrecision()
    {
        // Verify decimal precision for large values
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var pointer = new StreamPointer(new StreamIdentifier("Inventory", "precision-test"), 1);
        var largePrice = 99999999.99m;
        var state = new InventoryState(1000, 500, largePrice, DateTime.UtcNow);

        await store.SaveSnapshotAsync(pointer, state);
        var loaded = await store.LoadSnapshotAsync(pointer.Stream);

        Assert.NotNull(loaded);
        var loadedState = Assert.IsType<InventoryState>(loaded.State);
        Assert.Equal(largePrice, loadedState.UnitPrice);
    }

    [Fact]
    public async Task SaveSnapshot_DateTimeUtcPreserved()
    {
        // Verify UTC DateTime values are preserved exactly
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);

        var pointer = new StreamPointer(new StreamIdentifier("UserProfile", "datetime-test"), 1);
        var exactTime = new DateTime(2024, 6, 15, 13, 45, 22, 123, DateTimeKind.Utc);
        var state = new UserProfileState("test@example.com", "Test User", exactTime, 10);

        await store.SaveSnapshotAsync(pointer, state);
        var loaded = await store.LoadSnapshotAsync(pointer.Stream);

        Assert.NotNull(loaded);
        var loadedState = Assert.IsType<UserProfileState>(loaded.State);
        Assert.Equal(exactTime, loadedState.LastLoginAt);
        Assert.Equal(DateTimeKind.Utc, loadedState.LastLoginAt.Kind);
    }
}

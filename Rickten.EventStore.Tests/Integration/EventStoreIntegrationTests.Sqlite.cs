using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using System;
using Microsoft.Data.Sqlite;
using Rickten.Aggregator;

namespace Rickten.EventStore.Tests.Integration;

[Event("ProductSqlite", "Created", 1)]
public record ProductCreatedEventSqlite(string Name, decimal Price);

[Event("ProductSqlite", "Updated", 1)]
public record ProductUpdatedEventSqlite(decimal NewPrice);

[Aggregate("ProductSqlite")]
public record ProductStateSqlite(string Name, decimal Price);

/// <summary>
/// Integration tests using SQLite in-memory database.
/// These tests run fast without Docker and validate real relational database behaviors:
/// - ValueGeneratedOnAdd for event ID/global position
/// - Unique constraint on (StreamType, StreamIdentifier, Version) for optimistic concurrency
/// - Proper transaction handling and constraint enforcement
/// 
/// No external dependencies required - runs entirely in-memory!
/// </summary>
public class EventStoreIntegrationTestsSqlite : EventStoreIntegrationTestsBase, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventStoreDbContext> _options;

    public EventStoreIntegrationTestsSqlite()
    {
        // Use in-memory SQLite with a shared connection to persist data across operations
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Create the database schema
        using var context = new EventStoreDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    // Base class overrides
    protected override string AggregateType => "ProductSqlite";

    protected override void SkipIfNotAvailable()
    {
        // SQLite in-memory is always available - no need to skip
    }

    protected override EventStoreDbContext CreateContext() => new EventStoreDbContext(_options);

    protected override object CreateProductCreatedEvent(string name, decimal price) 
        => new ProductCreatedEventSqlite(name, price);

    protected override object CreateProductUpdatedEvent(decimal newPrice) 
        => new ProductUpdatedEventSqlite(newPrice);

    protected override object CreateProductState(string name, decimal price) 
        => new ProductStateSqlite(name, price);

    protected override void AssertProductCreatedEvent(object evt, string expectedName, decimal expectedPrice)
    {
        var productEvent = Assert.IsType<ProductCreatedEventSqlite>(evt);
        Assert.Equal(expectedName, productEvent.Name);
        Assert.Equal(expectedPrice, productEvent.Price);
    }
}

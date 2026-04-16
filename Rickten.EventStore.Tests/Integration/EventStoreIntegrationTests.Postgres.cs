using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using System;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;

namespace Rickten.EventStore.Tests.Integration;

[Event("ProductPostgres", "Created", 1)]
public record ProductCreatedEventPostgres(string Name, decimal Price);

[Event("ProductPostgres", "Updated", 1)]
public record ProductUpdatedEventPostgres(decimal NewPrice);

/// <summary>
/// Integration tests using PostgreSQL via Docker/Testcontainers.
/// Automatically spins up a PostgreSQL container for testing - no manual setup required!
/// 
/// Prerequisites:
/// - Docker Desktop must be installed and running
/// - Tests will be skipped if Docker is not available
/// </summary>
public class EventStoreIntegrationTestsPostgres : EventStoreIntegrationTestsBase, IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private DbContextOptions<EventStoreDbContext>? _options;
    private bool _isAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            // Spin up PostgreSQL 16 container
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("rickten_eventstore_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _container.StartAsync();

            _options = new DbContextOptionsBuilder<EventStoreDbContext>()
                .UseNpgsql(_container.GetConnectionString())
                .Options;

            // Create the database schema
            using var context = new EventStoreDbContext(_options);
            await context.Database.EnsureCreatedAsync();

            _isAvailable = true;
        }
        catch (Exception ex)
        {
            // Docker not available or other issue
            Console.WriteLine($"PostgreSQL container initialization failed: {ex.Message}");
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private void SkipIfNotAvailableInternal()
    {
        if (!_isAvailable)
        {
            Skip.If(true, "Docker is not available. Install Docker Desktop to run PostgreSQL container tests.");
        }
    }

    private EventStoreDbContext CreateContextInternal()
    {
        SkipIfNotAvailableInternal();
        return new EventStoreDbContext(_options!);
    }

    // Base class overrides
    protected override string AggregateType => "ProductPostgres";
    protected override void SkipIfNotAvailable() => SkipIfNotAvailableInternal();
    protected override EventStoreDbContext CreateContext() => CreateContextInternal();
    protected override object CreateProductCreatedEvent(string name, decimal price) => new ProductCreatedEventPostgres(name, price);
    protected override object CreateProductUpdatedEvent(decimal newPrice) => new ProductUpdatedEventPostgres(newPrice);
    protected override void AssertProductCreatedEvent(object evt, string expectedName, decimal expectedPrice)
    {
        var productEvent = Assert.IsType<ProductCreatedEventPostgres>(evt);
        Assert.Equal(expectedName, productEvent.Name);
        Assert.Equal(expectedPrice, productEvent.Price);
    }
}

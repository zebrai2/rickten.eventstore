using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Testcontainers.MsSql;
using Rickten.Aggregator;

namespace Rickten.EventStore.Tests.Integration;

[Event("ProductSqlServer", "Created", 1)]
public record ProductCreatedEventSqlServer(string Name, decimal Price);

[Event("ProductSqlServer", "Updated", 1)]
public record ProductUpdatedEventSqlServer(decimal NewPrice);

[Aggregate("ProductSqlServer")]
public record ProductStateSqlServer(string Name, decimal Price);

/// <summary>
/// Integration tests using SQL Server via Docker/Testcontainers.
/// Automatically spins up a SQL Server container for testing - no manual setup required!
/// 
/// Prerequisites:
/// - Docker Desktop must be installed and running
/// - Tests will be skipped if Docker is not available
/// 
/// This replaces the manual SQL Server tests that required environment variable configuration.
/// </summary>
public class EventStoreIntegrationTestsSqlServer : EventStoreIntegrationTestsBase, IAsyncLifetime
{
    private MsSqlContainer? _container;
    private DbContextOptions<EventStoreDbContext>? _options;
    private bool _isAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            // Spin up SQL Server 2022 container
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .WithPassword("YourStrong@Passw0rd")
                .Build();

            await _container.StartAsync();

            _options = new DbContextOptionsBuilder<EventStoreDbContext>()
                .UseSqlServer(_container.GetConnectionString())
                .Options;

            // Create the database schema
            using var context = new EventStoreDbContext(_options);
            await context.Database.EnsureCreatedAsync();

            _isAvailable = true;
        }
        catch (Exception ex)
        {
            // Docker not available or other issue
            Console.WriteLine($"SQL Server container initialization failed: {ex.Message}");
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
            Skip.If(true, "Docker is not available. Install Docker Desktop to run SQL Server container tests.");
        }
    }

    private EventStoreDbContext CreateContextInternal()
    {
        SkipIfNotAvailableInternal();
        return new EventStoreDbContext(_options!);
    }

    // Base class overrides
    protected override string AggregateType => "ProductSqlServer";
    protected override void SkipIfNotAvailable() => SkipIfNotAvailableInternal();
    protected override EventStoreDbContext CreateContext() => CreateContextInternal();
    protected override object CreateProductCreatedEvent(string name, decimal price) => new ProductCreatedEventSqlServer(name, price);
    protected override object CreateProductUpdatedEvent(decimal newPrice) => new ProductUpdatedEventSqlServer(newPrice);
    protected override object CreateProductState(string name, decimal price) => new ProductStateSqlServer(name, price);
    protected override void AssertProductCreatedEvent(object evt, string expectedName, decimal expectedPrice)
    {
        var productEvent = Assert.IsType<ProductCreatedEventSqlServer>(evt);
        Assert.Equal(expectedName, productEvent.Name);
        Assert.Equal(expectedPrice, productEvent.Price);
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rickten.EventStore;
using Rickten.EventStore.EntityFramework;
using System.Reflection;

namespace Rickten.Aggregator.Tests;

/// <summary>
/// Factory for creating test services with SQLite in-memory database.
/// Provides real database behavior (constraints, transactions, indexes) for testing.
/// </summary>
public static class TestServiceFactory
{
    /// <summary>
    /// Creates a service provider with all Event Store services configured to use SQLite in-memory database.
    /// The connection remains open for the lifetime of the connection object.
    /// </summary>
    /// <returns>A tuple containing the connection (must be kept alive) and the configured service provider.</returns>
    public static (SqliteConnection Connection, IServiceProvider ServiceProvider) CreateServiceProvider()
    {
        // Create in-memory SQLite database with shared connection
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();

        // Use the AddEventStore service installer with SQLite, including test assemblies
        services.AddEventStore(options =>
        {
            options.UseSqlite(connection);
        }, Assembly.GetExecutingAssembly());

        var serviceProvider = services.BuildServiceProvider();

        // Create the database schema
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();
        context.Database.EnsureCreated();

        return (connection, serviceProvider);
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.TypeMetadata;
using Rickten.Projector;

namespace Rickten.Runtime.Tests;

/// <summary>
/// Helper for creating test service providers with in-memory SQLite database.
/// </summary>
internal static class TestServiceFactory
{
    public static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Configure in-memory SQLite
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        services.AddSingleton(connection);
        services.AddDbContext<EventStoreDbContext>(options =>
            options.UseSqlite(connection));

        // Register TypeMetadata
        services.AddSingleton<ITypeMetadataRegistry>(provider =>
        {
            var builder = new TypeMetadataRegistryBuilder();
            builder.RegisterAssembly(typeof(TestReaction).Assembly);
            return builder.Build();
        });

        // Register EventStore services
        services.AddScoped<IEventStore, EntityFrameworkEventStore>();
        services.AddScoped<IProjectionStore, EntityFrameworkProjectionStore>();

        // Register test components
        services.AddScoped<TestReaction>();
        services.AddScoped<IStateFolder<TestAggregateState>, TestAggregateStateFolder>();
        services.AddScoped<ICommandDecider<TestAggregateState, TestProcessCommand>, TestCommandDecider>();

        var provider = services.BuildServiceProvider();

        // Initialize database
        var dbContext = provider.GetRequiredService<EventStoreDbContext>();
        dbContext.Database.EnsureCreated();

        return provider;
    }
}

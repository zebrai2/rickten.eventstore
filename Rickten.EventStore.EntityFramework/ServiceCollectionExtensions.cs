using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.TypeMetadata;
using System.Reflection;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Extension methods for configuring Event Store services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Event Store services (IEventStore, ISnapshotStore, and IProjectionStore) to the specified <see cref="IServiceCollection"/>.
    /// This is the recommended method that registers all three stores with a shared DbContext.
    /// All services are registered as Scoped (the safe default for DbContext-based services).
    /// Calling this method multiple times will not register duplicate services.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the Event Store.</param>
    /// <param name="assemblies">The assemblies to scan for attributed types. If not specified, only the calling assembly is scanned.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddEventStore(options =>
    /// {
    ///     options.UseSqlServer(connectionString);
    /// }, typeof(MyEvent).Assembly, typeof(MyAggregate).Assembly);
    /// </code>
    /// </example>
    public static IServiceCollection AddEventStore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsAction);

        // Register type metadata registry as singleton
        services.TryAddSingleton<ITypeMetadataRegistry>(sp =>
        {
            var builder = new TypeMetadataRegistryBuilder();

            // If no assemblies specified, use calling assembly
            if (assemblies == null || assemblies.Length == 0)
            {
                // Add common assemblies
                builder.AddAssembly(Assembly.GetCallingAssembly());
            }
            else
            {
                builder.AddAssemblies(assemblies);
            }

            return builder.Build();
        });

        // Use TryAddDbContext to prevent duplicate registrations
        services.AddDbContext<EventStoreDbContext>(optionsAction);

        // Use TryAdd to prevent duplicate store registrations
        services.TryAddScoped<IEventStore, EventStore>();
        services.TryAddScoped<ISnapshotStore, SnapshotStore>();
        services.TryAddScoped<IProjectionStore, ProjectionStore>();

        return services;
    }

    /// <summary>
    /// Adds all Event Store services using an in-memory database for testing purposes.
    /// All services are registered as Scoped.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseName">The name of the in-memory database.</param>
    /// <param name="assemblies">The assemblies to scan for attributed types. If not specified, only the calling assembly is scanned.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddEventStoreInMemory("TestDb", typeof(MyEvent).Assembly);
    /// </code>
    /// </example>
    public static IServiceCollection AddEventStoreInMemory(
        this IServiceCollection services,
        string databaseName,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        return services.AddEventStore(options =>
        {
            options.UseInMemoryDatabase(databaseName);
        }, assemblies);
    }

    /// <summary>
    /// Adds all Event Store services using SQL Server.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The connection string to use for SQL Server.</param>
    /// <param name="assemblies">The assemblies to scan for attributed types.</param>
    /// <param name="sqlServerOptionsAction">An optional action to configure SQL Server specific options.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddEventStoreSqlServer(connectionString, new[] { typeof(MyEvent).Assembly });
    /// </code>
    /// </example>
    public static IServiceCollection AddEventStoreSqlServer(
        this IServiceCollection services,
        string connectionString,
        Assembly[] assemblies,
        Action<Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddEventStore(options =>
        {
            options.UseSqlServer(connectionString, sqlServerOptionsAction);
        }, assemblies);
    }
}

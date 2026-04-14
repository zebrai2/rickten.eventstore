using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rickten.EventStore.EntityFramework;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Extension methods for configuring Event Store services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Event Store services (IEventStore, ISnapshotStore, and IProjectionStore) to the specified <see cref="IServiceCollection"/>.
    /// This is a convenience method that registers all three stores with a shared DbContext.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the Event Store.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddEventStore(options =>
    /// {
    ///     options.UseSqlServer(connectionString);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddEventStore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsAction);

        // Register the DbContext
        services.AddDbContext<EventStoreDbContext>(optionsAction);

        // Register all three store implementations
        services.AddScoped<IEventStore, EventStore>();
        services.AddScoped<ISnapshotStore, SnapshotStore>();
        services.AddScoped<IProjectionStore, ProjectionStore>();

        return services;
    }

    /// <summary>
    /// Adds only the IEventStore service to the specified <see cref="IServiceCollection"/>.
    /// Use this when you want to register stores separately with different configurations.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the Event Store.</param>
    /// <param name="lifetime">The lifetime with which to register the service. Defaults to Scoped.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddEventStoreOnly(options =>
    /// {
    ///     options.UseSqlServer(eventsConnectionString);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddEventStoreOnly(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsAction);

        // Register the DbContext
        services.AddDbContext<EventStoreDbContext>(optionsAction, lifetime);

        // Register only the event store
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                services.AddSingleton<IEventStore, EventStore>();
                break;
            case ServiceLifetime.Transient:
                services.AddTransient<IEventStore, EventStore>();
                break;
            default:
                services.AddScoped<IEventStore, EventStore>();
                break;
        }

        return services;
    }

    /// <summary>
    /// Adds only the ISnapshotStore service to the specified <see cref="IServiceCollection"/>.
    /// Use this when you want to register stores separately with different configurations.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the Snapshot Store.</param>
    /// <param name="lifetime">The lifetime with which to register the service. Defaults to Scoped.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddSnapshotStoreOnly(options =>
    /// {
    ///     options.UseSqlServer(snapshotsConnectionString);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddSnapshotStoreOnly(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsAction);

        // Register the DbContext (note: this will create a separate instance if called multiple times)
        services.AddDbContext<EventStoreDbContext>(optionsAction, lifetime);

        // Register only the snapshot store
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                services.AddSingleton<ISnapshotStore, SnapshotStore>();
                break;
            case ServiceLifetime.Transient:
                services.AddTransient<ISnapshotStore, SnapshotStore>();
                break;
            default:
                services.AddScoped<ISnapshotStore, SnapshotStore>();
                break;
        }

        return services;
    }

    /// <summary>
    /// Adds only the IProjectionStore service to the specified <see cref="IServiceCollection"/>.
    /// Use this when you want to register stores separately with different configurations.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the Projection Store.</param>
    /// <param name="lifetime">The lifetime with which to register the service. Defaults to Scoped.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddProjectionStoreOnly(options =>
    /// {
    ///     options.UseNpgsql(projectionsConnectionString);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddProjectionStoreOnly(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsAction);

        // Register the DbContext (note: this will create a separate instance if called multiple times)
        services.AddDbContext<EventStoreDbContext>(optionsAction, lifetime);

        // Register only the projection store
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                services.AddSingleton<IProjectionStore, ProjectionStore>();
                break;
            case ServiceLifetime.Transient:
                services.AddTransient<IProjectionStore, ProjectionStore>();
                break;
            default:
                services.AddScoped<IProjectionStore, ProjectionStore>();
                break;
        }

        return services;
    }
    /// <summary>
    /// Adds all Event Store services with optional lifetime configuration.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the Event Store.</param>
    /// <param name="contextLifetime">The lifetime with which to register the DbContext service in the container.</param>
    /// <param name="optionsLifetime">The lifetime with which to register the DbContextOptions service in the container.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddEventStore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        ServiceLifetime contextLifetime,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsAction);

        // Register the DbContext with custom lifetime
        services.AddDbContext<EventStoreDbContext>(optionsAction, contextLifetime, optionsLifetime);

        // Register all three store implementations with the same lifetime as context
        switch (contextLifetime)
        {
            case ServiceLifetime.Singleton:
                services.AddSingleton<IEventStore, EventStore>();
                services.AddSingleton<ISnapshotStore, SnapshotStore>();
                services.AddSingleton<IProjectionStore, ProjectionStore>();
                break;
            case ServiceLifetime.Transient:
                services.AddTransient<IEventStore, EventStore>();
                services.AddTransient<ISnapshotStore, SnapshotStore>();
                services.AddTransient<IProjectionStore, ProjectionStore>();
                break;
            default:
                services.AddScoped<IEventStore, EventStore>();
                services.AddScoped<ISnapshotStore, SnapshotStore>();
                services.AddScoped<IProjectionStore, ProjectionStore>();
                break;
        }

        return services;
    }

    /// <summary>
    /// Adds all Event Store services using an in-memory database for testing purposes.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseName">The name of the in-memory database.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddEventStoreInMemory("TestDb");
    /// </code>
    /// </example>
    public static IServiceCollection AddEventStoreInMemory(
        this IServiceCollection services,
        string databaseName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        return services.AddEventStore(options =>
        {
            options.UseInMemoryDatabase(databaseName);
        });
    }

    /// <summary>
    /// Adds all Event Store services using SQL Server.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The connection string to use for SQL Server.</param>
    /// <param name="sqlServerOptionsAction">An optional action to configure SQL Server specific options.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddEventStoreSqlServer(connectionString);
    /// </code>
    /// </example>
    public static IServiceCollection AddEventStoreSqlServer(
        this IServiceCollection services,
        string connectionString,
        Action<Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddEventStore(options =>
        {
            options.UseSqlServer(connectionString, sqlServerOptionsAction);
        });
    }
}

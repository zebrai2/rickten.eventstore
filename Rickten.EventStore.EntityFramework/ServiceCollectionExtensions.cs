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
    /// <param name="assemblies">The assemblies to scan for attributed types. Must contain at least one assembly.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentException">Thrown when no assemblies are provided.</exception>
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

        if (assemblies == null || assemblies.Length == 0)
        {
            throw new ArgumentException(
                "At least one assembly must be provided for type metadata registration. " +
                "Pass assemblies explicitly (e.g., typeof(MyEvent).Assembly) or use a marker-type overload (e.g., AddEventStore<TMarker>).",
                nameof(assemblies));
        }

        // Register type metadata registry as singleton with merging support
        // Get or create the assembly collection
        var registryAssemblies = services
            .Where(sd => sd.ServiceType == typeof(TypeMetadataRegistryAssemblies))
            .Select(sd => sd.ImplementationInstance as TypeMetadataRegistryAssemblies)
            .FirstOrDefault();

        if (registryAssemblies == null)
        {
            registryAssemblies = new TypeMetadataRegistryAssemblies();
            services.AddSingleton(registryAssemblies);
        }

        // Add new assemblies to the collection
        registryAssemblies.AddAssemblies(assemblies);

        // Register or replace the registry factory
        // Remove existing registry registration if present
        var existingRegistryDescriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(ITypeMetadataRegistry));
        if (existingRegistryDescriptor != null)
        {
            services.Remove(existingRegistryDescriptor);
        }

        // Add new registry registration that uses all accumulated assemblies
        services.AddSingleton<ITypeMetadataRegistry>(sp =>
        {
            var assembliesCollection = sp.GetRequiredService<TypeMetadataRegistryAssemblies>();
            var builder = new TypeMetadataRegistryBuilder();
            builder.AddAssemblies(assembliesCollection.GetAssemblies());
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
    /// Adds all Event Store services using a marker type to identify the assembly containing attributed types.
    /// </summary>
    /// <typeparam name="TMarker">A type from the assembly to scan for attributed types.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the Event Store.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// // MyEvent is a type in the assembly you want to scan
    /// services.AddEventStore&lt;MyEvent&gt;(options =>
    /// {
    ///     options.UseSqlServer(connectionString);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddEventStore<TMarker>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction)
    {
        return services.AddEventStore(optionsAction, typeof(TMarker).Assembly);
    }

    /// <summary>
    /// Adds all Event Store services using two marker types to identify assemblies containing attributed types.
    /// </summary>
    /// <typeparam name="TMarker1">A type from the first assembly to scan.</typeparam>
    /// <typeparam name="TMarker2">A type from the second assembly to scan.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the Event Store.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddEventStore<TMarker1, TMarker2>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction)
    {
        return services.AddEventStore(optionsAction, typeof(TMarker1).Assembly, typeof(TMarker2).Assembly);
    }

    /// <summary>
    /// Adds all Event Store services using three marker types to identify assemblies containing attributed types.
    /// </summary>
    /// <typeparam name="TMarker1">A type from the first assembly to scan.</typeparam>
    /// <typeparam name="TMarker2">A type from the second assembly to scan.</typeparam>
    /// <typeparam name="TMarker3">A type from the third assembly to scan.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the Event Store.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddEventStore<TMarker1, TMarker2, TMarker3>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction)
    {
        return services.AddEventStore(optionsAction, typeof(TMarker1).Assembly, typeof(TMarker2).Assembly, typeof(TMarker3).Assembly);
    }

    /// <summary>
    /// Adds all Event Store services using an in-memory database for testing purposes.
    /// All services are registered as Scoped.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseName">The name of the in-memory database.</param>
    /// <param name="assemblies">The assemblies to scan for attributed types. Must contain at least one assembly.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentException">Thrown when no assemblies are provided.</exception>
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
    /// Adds all Event Store services using an in-memory database with a marker type to identify the assembly.
    /// </summary>
    /// <typeparam name="TMarker">A type from the assembly to scan for attributed types.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseName">The name of the in-memory database.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddEventStoreInMemory<TMarker>(
        this IServiceCollection services,
        string databaseName)
    {
        return services.AddEventStoreInMemory(databaseName, typeof(TMarker).Assembly);
    }

    /// <summary>
    /// Adds all Event Store services using an in-memory database with marker types to identify assemblies.
    /// </summary>
    /// <typeparam name="TMarker1">A type from the first assembly to scan.</typeparam>
    /// <typeparam name="TMarker2">A type from the second assembly to scan.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseName">The name of the in-memory database.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddEventStoreInMemory<TMarker1, TMarker2>(
        this IServiceCollection services,
        string databaseName)
    {
        return services.AddEventStoreInMemory(databaseName, typeof(TMarker1).Assembly, typeof(TMarker2).Assembly);
    }

    /// <summary>
    /// Adds all Event Store services using an in-memory database with marker types to identify assemblies.
    /// </summary>
    /// <typeparam name="TMarker1">A type from the first assembly to scan.</typeparam>
    /// <typeparam name="TMarker2">A type from the second assembly to scan.</typeparam>
    /// <typeparam name="TMarker3">A type from the third assembly to scan.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseName">The name of the in-memory database.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddEventStoreInMemory<TMarker1, TMarker2, TMarker3>(
        this IServiceCollection services,
        string databaseName)
    {
        return services.AddEventStoreInMemory(databaseName, typeof(TMarker1).Assembly, typeof(TMarker2).Assembly, typeof(TMarker3).Assembly);
    }

    /// <summary>
    /// Adds all Event Store services using SQL Server.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The connection string to use for SQL Server.</param>
    /// <param name="assemblies">The assemblies to scan for attributed types. Must contain at least one assembly.</param>
    /// <param name="sqlServerOptionsAction">An optional action to configure SQL Server specific options.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentException">Thrown when no assemblies are provided.</exception>
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

    /// <summary>
    /// Adds all Event Store services using SQL Server with a marker type to identify the assembly.
    /// </summary>
    /// <typeparam name="TMarker">A type from the assembly to scan for attributed types.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The connection string to use for SQL Server.</param>
    /// <param name="sqlServerOptionsAction">An optional action to configure SQL Server specific options.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddEventStoreSqlServer<TMarker>(
        this IServiceCollection services,
        string connectionString,
        Action<Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    {
        return services.AddEventStoreSqlServer(connectionString, new[] { typeof(TMarker).Assembly }, sqlServerOptionsAction);
    }

    /// <summary>
    /// Adds all Event Store services using SQL Server with marker types to identify assemblies.
    /// </summary>
    /// <typeparam name="TMarker1">A type from the first assembly to scan.</typeparam>
    /// <typeparam name="TMarker2">A type from the second assembly to scan.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The connection string to use for SQL Server.</param>
    /// <param name="sqlServerOptionsAction">An optional action to configure SQL Server specific options.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddEventStoreSqlServer<TMarker1, TMarker2>(
        this IServiceCollection services,
        string connectionString,
        Action<Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    {
        return services.AddEventStoreSqlServer(connectionString, new[] { typeof(TMarker1).Assembly, typeof(TMarker2).Assembly }, sqlServerOptionsAction);
    }

    /// <summary>
    /// Adds all Event Store services using SQL Server with marker types to identify assemblies.
    /// </summary>
    /// <typeparam name="TMarker1">A type from the first assembly to scan.</typeparam>
    /// <typeparam name="TMarker2">A type from the second assembly to scan.</typeparam>
    /// <typeparam name="TMarker3">A type from the third assembly to scan.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The connection string to use for SQL Server.</param>
    /// <param name="sqlServerOptionsAction">An optional action to configure SQL Server specific options.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddEventStoreSqlServer<TMarker1, TMarker2, TMarker3>(
        this IServiceCollection services,
        string connectionString,
        Action<Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    {
        return services.AddEventStoreSqlServer(connectionString, new[] { typeof(TMarker1).Assembly, typeof(TMarker2).Assembly, typeof(TMarker3).Assembly }, sqlServerOptionsAction);
    }
}

/// <summary>
/// Internal helper class to accumulate assemblies across multiple AddEventStore calls.
/// </summary>
internal sealed class TypeMetadataRegistryAssemblies
{
    private readonly HashSet<Assembly> _assemblies = new();
    private readonly object _lock = new();

    public void AddAssemblies(IEnumerable<Assembly> assemblies)
    {
        lock (_lock)
        {
            foreach (var assembly in assemblies)
            {
                _assemblies.Add(assembly);
            }
        }
    }

    public IEnumerable<Assembly> GetAssemblies()
    {
        lock (_lock)
        {
            return _assemblies.ToArray();
        }
    }
}

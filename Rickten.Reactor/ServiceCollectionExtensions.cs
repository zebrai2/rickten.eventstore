using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Rickten.Reactor;

/// <summary>
/// Extension methods for registering reaction types in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all reaction types from the specified assemblies with the dependency injection container.
    /// Scans for concrete, non-abstract classes decorated with <see cref="ReactionAttribute"/> that inherit from
    /// <see cref="Reaction{TView, TCommand}"/>.
    /// Each reaction is registered as both its concrete type and its closed <see cref="Reaction{TView, TCommand}"/> base type.
    /// Calling this method multiple times with overlapping assemblies will not register duplicates.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="assemblies">The assemblies to scan for reaction types. Must contain at least one assembly.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when no assemblies are provided.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a class decorated with [Reaction] does not inherit from Reaction&lt;TView, TCommand&gt;.</exception>
    /// <remarks>
    /// <para>
    /// Important: Reaction types require <see cref="EventStore.TypeMetadata.ITypeMetadataRegistry"/> for validation during construction.
    /// The same assemblies containing reactions must also be registered with the Event Store setup:
    /// </para>
    /// <code>
    /// services.AddEventStore(
    ///     options => options.UseSqlServer(connectionString),
    ///     typeof(MyEvent).Assembly,
    ///     typeof(MyReaction).Assembly);
    ///     
    /// services.AddReactions(typeof(MyReaction).Assembly);
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register reactions from a specific assembly
    /// services.AddReactions(typeof(MyReaction).Assembly);
    /// 
    /// // Register reactions from multiple assemblies
    /// services.AddReactions(typeof(Reaction1).Assembly, typeof(Reaction2).Assembly);
    /// </code>
    /// </example>
    public static IServiceCollection AddReactions(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (assemblies == null || assemblies.Length == 0)
        {
            throw new ArgumentException(
                "At least one assembly must be provided for reaction registration. " +
                "Pass assemblies explicitly (e.g., typeof(MyReaction).Assembly) or use a marker-type overload (e.g., AddReactions<TMarker>).",
                nameof(assemblies));
        }

        // Register ReactionRunner as a singleton
        services.TryAddSingleton<ReactionRunner>();

        foreach (var reactionType in FindReactionTypes(assemblies))
        {
            var reactionBaseType = FindReactionBaseType(reactionType)
                ?? throw new InvalidOperationException(
                    $"Reaction type '{reactionType.FullName}' is decorated with [Reaction] but does not inherit from Reaction<TView, TCommand>.");

            // Register concrete type as transient (avoid duplicates)
            services.TryAddTransient(reactionType);

            // Register closed base type as enumerable transient (supports multiple implementations)
            services.TryAddEnumerable(ServiceDescriptor.Transient(reactionBaseType, reactionType));
        }

        return services;
    }

    /// <summary>
    /// Registers all reaction types from the assembly containing the marker type.
    /// </summary>
    /// <typeparam name="TMarker">A type from the assembly to scan for reaction types.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// // MyReaction is a type in the assembly you want to scan
    /// services.AddReactions&lt;MyReaction&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddReactions<TMarker>(this IServiceCollection services)
    {
        return services.AddReactions(typeof(TMarker).Assembly);
    }

    /// <summary>
    /// Registers all reaction types from the assemblies containing the marker types.
    /// </summary>
    /// <typeparam name="TMarker1">A type from the first assembly to scan.</typeparam>
    /// <typeparam name="TMarker2">A type from the second assembly to scan.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddReactions<TMarker1, TMarker2>(this IServiceCollection services)
    {
        return services.AddReactions(typeof(TMarker1).Assembly, typeof(TMarker2).Assembly);
    }

    /// <summary>
    /// Registers all reaction types from the assemblies containing the marker types.
    /// </summary>
    /// <typeparam name="TMarker1">A type from the first assembly to scan.</typeparam>
    /// <typeparam name="TMarker2">A type from the second assembly to scan.</typeparam>
    /// <typeparam name="TMarker3">A type from the third assembly to scan.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddReactions<TMarker1, TMarker2, TMarker3>(this IServiceCollection services)
    {
        return services.AddReactions(typeof(TMarker1).Assembly, typeof(TMarker2).Assembly, typeof(TMarker3).Assembly);
    }

    /// <summary>
    /// Finds all concrete, non-abstract classes decorated with [Reaction] in the specified assemblies.
    /// </summary>
    private static IEnumerable<Type> FindReactionTypes(Assembly[] assemblies)
    {
        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsClass &&
                    !type.IsAbstract &&
                    type.GetCustomAttribute<ReactionAttribute>() != null)
                {
                    yield return type;
                }
            }
        }
    }

    /// <summary>
    /// Finds the closed Reaction&lt;TView, TCommand&gt; base type by walking up the type hierarchy.
    /// Returns null if the type does not inherit from Reaction&lt;,&gt;.
    /// </summary>
    private static Type? FindReactionBaseType(Type reactionType)
    {
        var current = reactionType.BaseType;

        while (current != null)
        {
            if (current.IsGenericType &&
                current.GetGenericTypeDefinition() == typeof(Reaction<,>))
            {
                return current;
            }

            current = current.BaseType;
        }

        return null;
    }
}

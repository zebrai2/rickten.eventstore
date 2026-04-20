using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rickten.EventStore;

namespace Rickten.Projector;

/// <summary>
/// Extension methods for registering projection services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="ProjectionRunner"/> service with the dependency injection container.
    /// The runner is registered as scoped to match the typical lifetime of its dependencies.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// ProjectionRunner requires <see cref="EventStore.IEventStore"/> and <see cref="IProjectionStore"/>
    /// to be registered separately. These are typically registered through the Event Store setup:
    /// </para>
    /// <code>
    /// services.AddEventStore(
    ///     options => options.UseSqlServer(connectionString));
    ///     
    /// services.AddProjectionRunner();
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register projection runner
    /// services.AddProjectionRunner();
    /// 
    /// // Then inject it into your services
    /// public class MyProjectionService
    /// {
    ///     private readonly ProjectionRunner _runner;
    ///     
    ///     public MyProjectionService(ProjectionRunner runner)
    ///     {
    ///         _runner = runner;
    ///     }
    ///     
    ///     public async Task ExecuteAsync()
    ///     {
    ///         var (view, position) = await _runner.CatchUpAsync(myProjection);
    ///     }
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddProjectionRunner(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register ProjectionRunner as scoped to match the lifetime of its EF-backed dependencies
        services.TryAddScoped<ProjectionRunner>();

        return services;
    }
}

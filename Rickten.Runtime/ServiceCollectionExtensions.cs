using Microsoft.Extensions.DependencyInjection;

namespace Rickten.Runtime;

/// <summary>
/// Extension methods for configuring Rickten runtime services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Rickten runtime services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure the runtime builder.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRicktenRuntime(
        this IServiceCollection services,
        Action<IRicktenRuntimeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new RicktenRuntimeBuilder(services);
        configure(builder);
        return services;
    }
}

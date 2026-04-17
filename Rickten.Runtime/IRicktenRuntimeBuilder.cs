using Microsoft.Extensions.DependencyInjection;

namespace Rickten.Runtime;

/// <summary>
/// Builder interface for configuring Rickten runtime services.
/// </summary>
public interface IRicktenRuntimeBuilder
{
    /// <summary>
    /// Gets the service collection being configured.
    /// </summary>
    IServiceCollection Services { get; }
}

/// <summary>
/// Internal implementation of the Rickten runtime builder.
/// </summary>
internal sealed class RicktenRuntimeBuilder(IServiceCollection services)
    : IRicktenRuntimeBuilder
{
    public IServiceCollection Services { get; } = services;
}

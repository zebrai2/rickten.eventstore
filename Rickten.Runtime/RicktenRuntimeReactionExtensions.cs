using Microsoft.Extensions.DependencyInjection;
using Rickten.Reactor;

namespace Rickten.Runtime;

/// <summary>
/// Extension methods for adding reactions to the Rickten runtime.
/// </summary>
public static class RicktenRuntimeReactionExtensions
{
    /// <summary>
    /// Adds a reaction to the Rickten runtime as a hosted service.
    /// </summary>
    /// <typeparam name="TReaction">The reaction type.</typeparam>
    /// <typeparam name="TState">The aggregate state type.</typeparam>
    /// <typeparam name="TView">The projection view type.</typeparam>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="builder">The runtime builder.</param>
    /// <param name="configure">Optional configuration action for reaction options.</param>
    /// <returns>The runtime builder for chaining.</returns>
    public static IRicktenRuntimeBuilder AddReaction<TReaction, TState, TView, TCommand>(
        this IRicktenRuntimeBuilder builder,
        Action<RicktenReactionRuntimeOptions>? configure = null)
        where TReaction : Reaction<TView, TCommand>
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Create options instance for this specific reaction
        var options = new RicktenReactionRuntimeOptions();
        configure?.Invoke(options);

        // Register the hosted service with the options instance
        builder.Services.AddHostedService(provider =>
        {
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RicktenReactionHostedService<TReaction, TState, TView, TCommand>>>();
            return new RicktenReactionHostedService<TReaction, TState, TView, TCommand>(
                scopeFactory,
                logger,
                options);
        });

        return builder;
    }
}

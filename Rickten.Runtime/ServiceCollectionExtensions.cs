using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rickten.Reactor;

namespace Rickten.Runtime;

/// <summary>
/// Extension methods for configuring Rickten runtime services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Rickten runtime services with default options.
    /// This must be called before adding hosted reactions.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRicktenRuntime(this IServiceCollection services)
    {
        return services.AddRicktenRuntime(options => { });
    }

    /// <summary>
    /// Adds the Rickten runtime services with configuration.
    /// This must be called before adding hosted reactions.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure runtime options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddRicktenRuntime(options => 
    /// {
    ///     options.DefaultPollingInterval = TimeSpan.FromSeconds(10);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddRicktenRuntime(
        this IServiceCollection services,
        Action<RicktenRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        return services;
    }

    /// <summary>
    /// Adds a hosted reaction that will run continuously in the background.
    /// The reaction will call ReactionRunner.CatchUpAsync on the configured polling interval.
    /// </summary>
    /// <typeparam name="TReaction">The concrete reaction type (must be registered in DI).</typeparam>
    /// <typeparam name="TState">The aggregate state type.</typeparam>
    /// <typeparam name="TView">The projection view type.</typeparam>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="pollingInterval">Optional polling interval override for this specific reaction. If null, uses the value from ReactionAttribute.PollingIntervalMilliseconds, or the default from RicktenRuntimeOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The reaction type must be registered in the DI container (typically via AddReactions).
    /// The AggregateCommandExecutor for the state and command types must also be registered.
    /// </para>
    /// <para>
    /// Each catch-up iteration creates a new service scope, allowing scoped dependencies to be resolved fresh.
    /// If a catch-up fails, the error is logged and the service continues running, retrying on the next interval.
    /// </para>
    /// <para>
    /// Polling interval resolution order:
    /// 1. Parameter override (if provided)
    /// 2. ReactionAttribute.PollingIntervalMilliseconds (if set on the reaction class)
    /// 3. RicktenRuntimeOptions.DefaultPollingInterval
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Use polling interval from ReactionAttribute or runtime default
    /// services.AddHostedReaction&lt;MyReaction, MyState, MyView, MyCommand&gt;();
    /// 
    /// // Override with specific interval
    /// services.AddHostedReaction&lt;FastReaction, MyState, MyView, MyCommand&gt;(
    ///     pollingInterval: TimeSpan.FromSeconds(1));
    /// </code>
    /// </example>
    public static IServiceCollection AddHostedReaction<TReaction, TState, TView, TCommand>(
        this IServiceCollection services,
        TimeSpan? pollingInterval = null)
        where TReaction : Reaction<TView, TCommand>
    {
        ArgumentNullException.ThrowIfNull(services);

        // Read polling interval from attribute if not overridden
        TimeSpan? effectiveInterval = pollingInterval;
        if (!pollingInterval.HasValue)
        {
            var reactionAttr = typeof(TReaction).GetCustomAttributes(typeof(ReactionAttribute), false)
                .FirstOrDefault() as ReactionAttribute;

            if (reactionAttr?.PollingIntervalMilliseconds > 0)
            {
                effectiveInterval = TimeSpan.FromMilliseconds(reactionAttr.PollingIntervalMilliseconds);
            }
        }

        services.AddHostedService(provider =>
        {
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RicktenRuntimeOptions>>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ReactionHostedService<TReaction, TState, TView, TCommand>>>();
            var waiter = provider.GetRequiredService<IWaiter>();

            return new ReactionHostedService<TReaction, TState, TView, TCommand>(
                scopeFactory,
                options,
                logger,
                waiter,
                effectiveInterval);
        });

        return services;
    }
}

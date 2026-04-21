using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rickten.Aggregator;
using Rickten.Reactor;

namespace Rickten.Runtime;

/// <summary>
/// Background service that continuously runs a specific reaction by calling ReactionRunner.CatchUpAsync on an interval.
/// Uses a scoped service provider per iteration, respects cancellation, and logs failures while continuing execution.
/// </summary>
/// <typeparam name="TReaction">The concrete reaction type.</typeparam>
/// <typeparam name="TState">The aggregate state type.</typeparam>
/// <typeparam name="TView">The projection view type.</typeparam>
/// <typeparam name="TCommand">The command type.</typeparam>
internal class ReactionHostedService<TReaction, TState, TView, TCommand>(
    IServiceScopeFactory scopeFactory,
    IOptions<RicktenRuntimeOptions> options,
    ILogger<ReactionHostedService<TReaction, TState, TView, TCommand>> logger,
    IWaiter waiter,
    TimeSpan? pollingInterval = null) : BackgroundService
    where TReaction : Reaction<TView, TCommand>
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly ILogger<ReactionHostedService<TReaction, TState, TView, TCommand>> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IWaiter _waiter = waiter ?? throw new ArgumentNullException(nameof(waiter));
    private readonly TimeSpan _pollingInterval = pollingInterval ?? options.Value.DefaultPollingInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reactionName = typeof(TReaction).Name;
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Starting hosted reaction '{ReactionName}' with polling interval {PollingInterval}",
                reactionName, _pollingInterval);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                var runner = scope.ServiceProvider.GetRequiredService<ReactionRunner>();
                var reaction = scope.ServiceProvider.GetRequiredService<TReaction>();
                var executor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<TState, TCommand>>();

                var position = await runner.CatchUpAsync(
                    reaction,
                    executor,
                    stoppingToken);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Hosted reaction '{ReactionName}' caught up to position {Position}",
                        reactionName, position);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                    "Hosted reaction '{ReactionName}' stopping due to cancellation",
                    reactionName);
                }
                break;
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(
                        ex,
                        "Hosted reaction '{ReactionName}' failed during catch-up. Will retry after {PollingInterval}",
                        reactionName, _pollingInterval);
                }
            }

            try
            {
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Hosted reaction '{ReactionName}' stopping during delay",
                        reactionName);
                }
                break;
            }
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
            "Hosted reaction '{ReactionName}' stopped",
            reactionName);
        }
    }
}

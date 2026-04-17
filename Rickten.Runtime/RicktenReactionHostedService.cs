using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.Projector;
using Rickten.Reactor;

namespace Rickten.Runtime;

/// <summary>
/// Hosted service that runs a reaction in a background loop.
/// </summary>
/// <typeparam name="TReaction">The reaction type.</typeparam>
/// <typeparam name="TState">The aggregate state type.</typeparam>
/// <typeparam name="TView">The projection view type.</typeparam>
/// <typeparam name="TCommand">The command type.</typeparam>
public sealed class RicktenReactionHostedService<TReaction, TState, TView, TCommand>
    : BackgroundService
    where TReaction : Reaction<TView, TCommand>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RicktenReactionHostedService<TReaction, TState, TView, TCommand>> _logger;
    private readonly RicktenReactionRuntimeOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RicktenReactionHostedService{TReaction, TState, TView, TCommand}"/> class.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for creating scopes per pass.</param>
    /// <param name="logger">Logger for the hosted service.</param>
    /// <param name="options">Runtime options for this reaction.</param>
    public RicktenReactionHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<RicktenReactionHostedService<TReaction, TState, TView, TCommand>> logger,
        RicktenReactionRuntimeOptions options)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Runs the reaction catch-up once in a fresh DI scope.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The global position reached.</returns>
    public async Task<long> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        // Create a fresh DI scope for this pass
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        // Resolve required services
        var eventStore = services.GetRequiredService<IEventStore>();
        var projectionStore = services.GetRequiredService<IProjectionStore>();
        var reaction = services.GetRequiredService<TReaction>();
        var folder = services.GetRequiredService<IStateFolder<TState>>();
        var decider = services.GetRequiredService<ICommandDecider<TState, TCommand>>();

        // Resolve optional snapshot store
        var snapshotStore = services.GetService<ISnapshotStore>();

        // Create a logger for the ReactionRunner
        var runnerLogger = services.GetRequiredService<ILogger<TReaction>>();

        // Run the reaction catch-up
        var position = await ReactionRunner.CatchUpAsync<TState, TView, TCommand>(
            eventStore,
            projectionStore,
            reaction,
            folder,
            decider,
            snapshotStore,
            _options.ReactionName,
            runnerLogger,
            cancellationToken);

        return position;
    }

    /// <summary>
    /// Executes the reaction in a loop until cancellation.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for stopping the service.</param>
    /// <returns>A task representing the background execution.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // If disabled, return immediately
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Reaction runtime for {ReactionType} is disabled, not starting.",
                typeof(TReaction).Name);
            return;
        }

        _logger.LogInformation(
            "Reaction runtime for {ReactionType} starting with polling interval {PollingInterval}.",
            typeof(TReaction).Name,
            _options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);

                // Delay before next pass
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Clean shutdown
                _logger.LogInformation(
                    "Reaction runtime for {ReactionType} stopping due to cancellation.",
                    typeof(TReaction).Name);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error in reaction runtime for {ReactionType}.",
                    typeof(TReaction).Name);

                if (_options.ErrorBehavior == RicktenRuntimeErrorBehavior.Stop)
                {
                    _logger.LogCritical(
                        "ErrorBehavior is Stop, rethrowing exception for {ReactionType}.",
                        typeof(TReaction).Name);
                    throw;
                }
                else
                {
                    _logger.LogWarning(
                        "ErrorBehavior is Retry, delaying {ErrorDelay} before retry for {ReactionType}.",
                        _options.ErrorDelay,
                        typeof(TReaction).Name);

                    try
                    {
                        await Task.Delay(_options.ErrorDelay, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation(
                            "Reaction runtime for {ReactionType} stopping during error delay.",
                            typeof(TReaction).Name);
                        return;
                    }
                }
            }
        }
    }
}

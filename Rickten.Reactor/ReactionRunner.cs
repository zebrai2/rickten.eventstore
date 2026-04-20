using Microsoft.Extensions.Logging;
using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.Projector;

namespace Rickten.Reactor;

/// <summary>
/// Executes reactions using projection-based stream selection.
/// <para>
/// Each reaction maintains TWO checkpoints:
/// <list type="bullet">
/// <item><description>Reaction checkpoint in <see cref="IReactionRepository"/> - Trigger and projection positions</description></item>
/// <item><description>Projection view in <see cref="IProjectionStore"/> (reaction namespace) - The projection state</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Historical Projection Guarantee:</strong> Reactions evaluate triggers against the projection state
/// as of the trigger's global position, not as of the latest event. This ensures that when replaying triggers,
/// the projection view represents the historical state at that moment in time.
/// </para>
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ReactionRunner"/> class.
/// </remarks>
/// <param name="eventStore">The event store to load events from.</param>
/// <param name="projectionStore">The projection store for managing projection state.</param>
/// <param name="projectionRunner">The projection runner for managing projection state synchronization.</param>
/// <param name="reactionRepository">The reaction repository for managing reaction checkpoints.</param>
/// <param name="logger">Optional logger for diagnostic information.</param>
public sealed class ReactionRunner(IEventStore eventStore,
                                   IProjectionStore projectionStore,
                                   ProjectionRunner projectionRunner,
                                   IReactionRepository reactionRepository,
                                   ILogger<ReactionRunner>? logger = null)
{
    private readonly IEventStore _eventStore = eventStore;
    private readonly IProjectionStore _projectionStore = projectionStore;
    private readonly ProjectionRunner _projectionRunner = projectionRunner;
    private readonly IReactionRepository _reactionRepository = reactionRepository;
    private readonly ILogger<ReactionRunner>? _logger = logger;

    /// <summary>
    /// Catches up a reaction from its last checkpoint.
    /// <para>
    /// The reaction maintains separate checkpoints for execution state and projection view:
    /// <list type="number">
    /// <item><description><strong>Reaction checkpoint</strong> (in <see cref="IReactionRepository"/>): Stores trigger and projection positions for the reaction.</description></item>
    /// <item><description><strong>Projection view</strong> (in <see cref="IProjectionStore"/> with "reaction" namespace): The materialized projection state at the projection position.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// For each trigger event:
    /// <list type="bullet">
    /// <item><description>Ensures the reaction's private projection represents state at the trigger's global position (historical accuracy)</description></item>
    /// <item><description>Selects target aggregate streams using the projection view</description></item>
    /// <item><description>Executes a command against each selected stream through the aggregator pipeline</description></item>
    /// <item><description>Saves the reaction checkpoint after all commands succeed</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Checkpoint Drift Detection:</strong> If the projection checkpoint is ahead of the trigger checkpoint
    /// (indicating a previous failure), the projection is automatically rebuilt to the trigger position to preserve
    /// historical accuracy. This scenario is logged when a logger is provided.
    /// </para>
    /// </summary>
    /// <typeparam name="TState">The target aggregate state type.</typeparam>
    /// <typeparam name="TView">The projection view type used to select target streams.</typeparam>
    /// <typeparam name="TCommand">The command type to execute against target aggregates.</typeparam>
    /// <param name="reaction">The reaction to execute. The reaction name from the [Reaction] attribute is used for checkpoint keys. The reaction owns its projection via the <see cref="Reaction{TView,TCommand}.Projection"/> property.</param>
    /// <param name="executor">The aggregate command executor for executing commands against target aggregates. The executor is configured with folder, decider, registry, and snapshot store for the target aggregate type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The global position of the last successfully processed trigger event.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Projection Ownership:</strong> The reaction conceptually owns its projection. If the projection has
    /// dependencies (e.g., other services), use constructor injection in the reaction class and instantiate the
    /// projection with those dependencies.
    /// </para>
    /// <para>
    /// <strong>At-Least-Once Guarantee:</strong> Commands may be replayed if a failure occurs before checkpoints
    /// are saved. Ensure commands are idempotent.
    /// </para>
    /// </remarks>
    public async Task<long> CatchUpAsync<TState, TView, TCommand>(
        Reaction<TView, TCommand> reaction,
        AggregateCommandExecutor<TState, TCommand> executor,
        CancellationToken cancellationToken = default)
    {
        var checkpoint = await _reactionRepository.LoadCheckpointAsync(reaction.Name, cancellationToken)
            ?? CreateInitialState(reaction.Name);

        var projectionCheckpoint = await _projectionStore.LoadProjectionAsync<TView>(reaction.Name, "reaction", cancellationToken);


        var projectionView = projectionCheckpoint != null ? projectionCheckpoint.State : reaction.Projection.InitialView();
        var projectionPosition = projectionCheckpoint?.GlobalPosition ?? 0;
        var reactionPosition = checkpoint.TriggerPosition;
        var aggregateFilter = reaction.Projection.AggregateTypeFilter;
        var eventTypeFilter = reaction.Projection.EventTypeFilter;

        if (projectionPosition > reactionPosition && _logger?.IsEnabled(LogLevel.Warning) == true)
        {
            _logger.LogWarning(
                "Reaction '{ReactionName}' projection drift detected. Projection is at position {ProjectionPosition} but reaction last ran at {ReactionPosition}. " +
                "This may indicate the reaction checkpoint was reset or corrupted. Rebuilding projection from scratch to position {ReactionPosition}.",
                reaction.Name, projectionPosition, reactionPosition, reactionPosition);
        }

        (projectionView, projectionPosition) = await _projectionRunner.SyncToPositionAsync(
            reaction.Projection,
            projectionView,
            projectionPosition,
            reactionPosition,
            "reaction",
            cancellationToken);


        var lastReactionPosition = reactionPosition;

        var mergedEvents = _eventStore.LoadAllMergedAsync(
            reactionPosition,
            [
                (aggregateFilter, eventTypeFilter),  // Filter 0: Projection events
                (null, reaction.EventTypeFilter)     // Filter 1: Trigger events
            ],
            cancellationToken);

        await foreach (var (streamEvent, matchingFilters) in mergedEvents)
        {
            bool isProjectionEvent = matchingFilters.Contains(0);
            bool isTriggerEvent = matchingFilters.Contains(1);

            if (isProjectionEvent && streamEvent.GlobalPosition > projectionPosition)
            {
                projectionView = reaction.Projection.Apply(projectionView, streamEvent);
                projectionPosition = streamEvent.GlobalPosition;
            }

            if (isTriggerEvent && streamEvent.GlobalPosition > reactionPosition)
            {
                var commands = reaction.Process(projectionView, streamEvent);
                var reactionMetadata = new List<AppendMetadata>();

                AddCorrelationIdMetadata(streamEvent, reactionMetadata);
                AddEventIdMetadata(streamEvent, reactionMetadata);

                // Add reaction identity metadata
                if (reaction.WireName != null)
                {
                    reactionMetadata.Add(new AppendMetadata(EventMetadataKeys.ReactionWireName, reaction.WireName));
                }

                // Execute commands against each selected stream
                foreach (var (targetStream, command) in commands)
                {
                    await executor.ExecuteAsync(
                        targetStream,
                        command,
                        reactionMetadata,
                        cancellationToken);
                }

                lastReactionPosition = streamEvent.GlobalPosition;

                await SaveReactionStateAsync(reaction.Name, lastReactionPosition, projectionPosition, projectionView, cancellationToken);
            }
        }

        // Final save to persist projection-only updates
        await SaveReactionStateAsync(reaction.Name, lastReactionPosition, projectionPosition, projectionView, cancellationToken);

        return lastReactionPosition;
    }

    private static ReactionCheckpoint CreateInitialState(string reactionName)
    {
        return new ReactionCheckpoint(
                ReactionName: reactionName,
                TriggerPosition: 0,
                ProjectionPosition: 0);
    }

    private void AddCorrelationIdMetadata(StreamEvent streamEvent, List<AppendMetadata> reactionMetadata)
    {
        var triggerCorrelationId = streamEvent.Metadata.GetCorrelationId();
        if (triggerCorrelationId != null)
        {
            reactionMetadata.Add(new AppendMetadata(EventMetadataKeys.CorrelationId, triggerCorrelationId));
        }
        else
        {
            if (_logger?.IsEnabled(LogLevel.Critical) == true)
            {
                _logger.LogCritical("Trigger event at position {Position} in stream {Stream} is missing CorrelationId. " +
                                    "This indicates a broken event metadata system. All events should have CorrelationId.",
                                    streamEvent.GlobalPosition, streamEvent.StreamPointer.Stream);
            }
        }
    }

    private void AddEventIdMetadata(StreamEvent streamEvent, List<AppendMetadata> reactionMetadata)
    {
        var triggerEventId = streamEvent.Metadata.GetSystemEventId();
        if (triggerEventId.HasValue)
        {
            reactionMetadata.Add(new AppendMetadata(EventMetadataKeys.CausationId, triggerEventId.Value));
        }
        else
        {
            if (_logger?.IsEnabled(LogLevel.Critical) == true)
            {
                _logger.LogCritical("Trigger event at position {Position} in stream {Stream} is missing system EventId. " +
                                    "This indicates a broken event metadata system. All events should have system EventId.",
                                    streamEvent.GlobalPosition, streamEvent.StreamPointer.Stream);
            }
        }
    }

    /// <summary>
    /// Saves both the reaction checkpoint and the projection view to keep them in sync.
    /// </summary>
    private async Task SaveReactionStateAsync<TView>(
        string reactionName,
        long triggerPosition,
        long projectionPosition,
        TView projectionView,
        CancellationToken cancellationToken)
    {
        await _reactionRepository.SaveCheckpointAsync(
            new ReactionCheckpoint(reactionName, triggerPosition, projectionPosition),
            cancellationToken);
        await _projectionStore.SaveProjectionAsync(
            reactionName,
            projectionPosition,
            projectionView,
            "reaction",
            cancellationToken);
    }
}

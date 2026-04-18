using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.Projector;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Rickten.Reactor;

/// <summary>
/// Utilities for executing reactions using projection-based stream selection.
/// <para>
/// Each reaction maintains TWO checkpoints in the "reaction" namespace:
/// <list type="bullet">
/// <item><description><c>{reactionName}:trigger</c> - Last trigger event fully reacted (commands executed and saved)</description></item>
/// <item><description><c>{reactionName}:projection</c> - Last event applied to the private reaction projection</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Historical Projection Guarantee:</strong> Reactions evaluate triggers against the projection state
/// as of the trigger's global position, not as of the latest event. This ensures that when replaying triggers,
/// the projection view represents the historical state at that moment in time.
/// </para>
/// </summary>
/// <remarks>
/// Public projections use the "system" namespace and are managed by <see cref="Projector.ProjectionRunner"/>.
/// Reaction projections are private and isolated in the "reaction" namespace.
/// </remarks>
public static class ReactionRunner
{
    /// <summary>
    /// Catches up a reaction from its last checkpoint.
    /// <para>
    /// The reaction maintains its own private projection state for stream selection, storing two checkpoints:
    /// <list type="number">
    /// <item><description><strong>Trigger checkpoint</strong> (<c>{reactionName}:trigger</c>): The global position of the last trigger event that was fully processed (all commands executed and saved).</description></item>
    /// <item><description><strong>Projection checkpoint</strong> (<c>{reactionName}:projection</c>): The global position of the last event applied to the reaction's private projection view.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// For each trigger event:
    /// <list type="bullet">
    /// <item><description>Ensures the reaction's private projection represents state at the trigger's global position (historical accuracy)</description></item>
    /// <item><description>Selects target aggregate streams using the projection view</description></item>
    /// <item><description>Executes a command against each selected stream through the aggregator pipeline</description></item>
    /// <item><description>Saves both checkpoints after all commands succeed (atomic commit)</description></item>
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
    /// <param name="eventStore">The event store to load events from.</param>
    /// <param name="projectionStore">The projection store for managing checkpoints (reactions use "reaction" namespace).</param>
    /// <param name="reaction">The reaction to execute. The reaction owns its projection via the <see cref="Reaction{TView,TCommand}.Projection"/> property.</param>
    /// <param name="folder">The state folder for the target aggregate type.</param>
    /// <param name="decider">The command decider for the target aggregate type.</param>
    /// <param name="registry">The type metadata registry for resolving event and aggregate metadata.</param>
    /// <param name="snapshotStore">Optional snapshot store for target aggregate optimization.</param>
    /// <param name="reactionName">The name to use for storing the reaction checkpoints (defaults to reaction's name from [Reaction] attribute).</param>
    /// <param name="logger">Optional logger for diagnostic information. Recommended for production to detect checkpoint drift and rebuild scenarios.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The global position of the last successfully processed trigger event.</returns>
    /// <exception cref="ArgumentException">Thrown when reaction name cannot be determined from parameter or attribute.</exception>
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
    public static async Task<long> CatchUpAsync<TState, TView, TCommand>(
        IEventStore eventStore,
        IProjectionStore projectionStore,
        Reaction<TView, TCommand> reaction,
        IStateFolder<TState> folder,
        ICommandDecider<TState, TCommand> decider,
        EventStore.TypeMetadata.ITypeMetadataRegistry registry,
        ISnapshotStore? snapshotStore = null,
        string? reactionName = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        // Determine reaction name
        var name = reactionName ?? reaction.ReactionName;
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException(
                "Reaction name must be provided either via parameter or [Reaction] attribute",
                nameof(reactionName));
        }

        // Load reaction checkpoint (last trigger event successfully processed)
        // Stored as a projection with key "{reactionName}:trigger" in "reaction" namespace
        var reactionCheckpoint = await projectionStore.LoadProjectionAsync<long>($"{name}:trigger", "reaction", cancellationToken);
        var reactionPosition = reactionCheckpoint?.State ?? 0;

        // Load reaction's private projection state
        // Stored as a projection with key "{reactionName}:projection" in "reaction" namespace
        var projectionCheckpoint = await projectionStore.LoadProjectionAsync<TView>($"{name}:projection", "reaction", cancellationToken);
        TView projectionView;
        long projectionPosition;

        if (projectionCheckpoint != null)
        {
            projectionView = projectionCheckpoint.State;
            projectionPosition = projectionCheckpoint.GlobalPosition;
        }
        else
        {
            projectionView = reaction.Projection.InitialView();
            projectionPosition = 0;
        }

        // Get projection filters if available for optimized event loading
        string[]? aggregateFilter = null;
        string[]? eventTypeFilter = null;
        if (reaction.Projection is Projector.Projection<TView> proj)
        {
            aggregateFilter = proj.AggregateTypeFilter;
            eventTypeFilter = proj.EventTypeFilter;
        }

        // If projection is ahead of reaction, rebuild projection to reaction position
        // This ensures projection represents correct historical state when processing old triggers
        long fromPosition;
        if (projectionPosition > reactionPosition)
        {
            // Log warning: projection being ahead indicates something unexpected happened
            logger?.LogWarning(
                "Reaction '{ReactionName}' projection is ahead of reaction checkpoint. " +
                "Projection position: {ProjectionPosition}, Reaction position: {ReactionPosition}. " +
                "Rebuilding projection from scratch to ensure historical accuracy. " +
                "This may indicate a previous reaction failure or manual intervention.",
                name, projectionPosition, reactionPosition);

            // Rebuild projection up to reaction position using ProjectionRunner
            (projectionView, projectionPosition) = await ProjectionRunner.RebuildUntilAsync(
                eventStore,
                reaction.Projection,
                reactionPosition,
                fromGlobalPosition: 0,
                cancellationToken);

            logger?.LogInformation(
                "Reaction '{ReactionName}' projection rebuilt to position {ProjectionPosition}",
                name, projectionPosition);

            // Now start processing from reaction position
            fromPosition = reactionPosition;
        }
        else
        {
            // Projection is behind or equal to reaction, start from projection position
            fromPosition = projectionPosition;
        }

        var lastReactionPosition = reactionPosition;

        // Load two separate event streams:
        // 1. Projection events (with projection's filters) - to keep projection state current
        // 2. Trigger events (with reaction's filters) - to execute commands
        // Merge them by global position for ordered processing

        var mergedEvents = MergeEventStreamsByPosition(
            eventStore.LoadAllAsync(fromPosition, aggregateFilter, eventTypeFilter, cancellationToken),
            eventStore.LoadAllAsync(fromPosition, null, reaction.EventTypeFilter, cancellationToken),
            cancellationToken);

        await foreach (var (streamEvent, isProjectionEvent, isTriggerEvent) in mergedEvents)
        {
            // Update projection view if this is a projection event
            if (isProjectionEvent && streamEvent.GlobalPosition > projectionPosition)
            {
                projectionView = reaction.Projection.Apply(projectionView, streamEvent);
                projectionPosition = streamEvent.GlobalPosition;
            }

            // Process trigger if this is a trigger event
            if (isTriggerEvent && streamEvent.GlobalPosition > reactionPosition)
            {
                // Process trigger event with current projection view
                var commands = reaction.Process(projectionView, streamEvent);

                // Build metadata for outgoing commands from the trigger event
                // Propagate CorrelationId from trigger, set CausationId to trigger's EventId
                var reactionMetadata = new List<AppendMetadata>();

                // Propagate CorrelationId if present in trigger event
                var triggerCorrelationId = streamEvent.Metadata.GetCorrelationId();
                if (triggerCorrelationId.HasValue)
                {
                    reactionMetadata.Add(new AppendMetadata(EventMetadataKeys.CorrelationId, triggerCorrelationId.Value));
                }

                // Set CausationId to the trigger event's system-generated EventId
                // Use GetSystemEventId to ensure we're using the authoritative system EventId
                var triggerEventId = streamEvent.Metadata.GetSystemEventId();
                if (triggerEventId.HasValue)
                {
                    reactionMetadata.Add(new AppendMetadata(EventMetadataKeys.CausationId, triggerEventId.Value));
                }

                // Execute commands against each selected stream
                foreach (var (targetStream, command) in commands)
                {
                    await StateRunner.ExecuteAsync(
                        eventStore,
                        folder,
                        decider,
                        targetStream,
                        command,
                        registry,
                        snapshotStore,
                        metadata: reactionMetadata,
                        cancellationToken);
                }

                // Save both checkpoints after all commands for this trigger succeed
                lastReactionPosition = streamEvent.GlobalPosition;
                await projectionStore.SaveProjectionAsync($"{name}:trigger", lastReactionPosition, lastReactionPosition, "reaction", cancellationToken);
                await projectionStore.SaveProjectionAsync($"{name}:projection", projectionPosition, projectionView, "reaction", cancellationToken);
            }
        }

        // Save final projection state even if no triggers were processed
        // (the projection may have advanced without new triggers)
        if (projectionPosition > (projectionCheckpoint?.GlobalPosition ?? 0))
        {
            await projectionStore.SaveProjectionAsync($"{name}:projection", projectionPosition, projectionView, "reaction", cancellationToken);
        }

        return lastReactionPosition;
    }

    /// <summary>
    /// Merges two event streams, yielding events in global position order.
    /// When the same event appears in both streams (same global position), it's yielded once with both flags set.
    /// </summary>
    private static async IAsyncEnumerable<(StreamEvent Event, bool IsProjectionEvent, bool IsTriggerEvent)> MergeEventStreamsByPosition(
        IAsyncEnumerable<StreamEvent> projectionStream,
        IAsyncEnumerable<StreamEvent> triggerStream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var projectionEnumerator = projectionStream.GetAsyncEnumerator(cancellationToken);
        await using var triggerEnumerator = triggerStream.GetAsyncEnumerator(cancellationToken);

        bool hasProjection = await projectionEnumerator.MoveNextAsync();
        bool hasTrigger = await triggerEnumerator.MoveNextAsync();

        while (hasProjection || hasTrigger)
        {
            if (hasProjection && hasTrigger)
            {
                var projPos = projectionEnumerator.Current.GlobalPosition;
                var trigPos = triggerEnumerator.Current.GlobalPosition;

                if (projPos == trigPos)
                {
                    // Same event in both streams - yield once with both flags
                    yield return (projectionEnumerator.Current, IsProjectionEvent: true, IsTriggerEvent: true);
                    hasProjection = await projectionEnumerator.MoveNextAsync();
                    hasTrigger = await triggerEnumerator.MoveNextAsync();
                }
                else if (projPos < trigPos)
                {
                    // Projection event comes first
                    yield return (projectionEnumerator.Current, IsProjectionEvent: true, IsTriggerEvent: false);
                    hasProjection = await projectionEnumerator.MoveNextAsync();
                }
                else
                {
                    // Trigger event comes first
                    yield return (triggerEnumerator.Current, IsProjectionEvent: false, IsTriggerEvent: true);
                    hasTrigger = await triggerEnumerator.MoveNextAsync();
                }
            }
            else if (hasProjection)
            {
                // Only projection events remaining
                yield return (projectionEnumerator.Current, IsProjectionEvent: true, IsTriggerEvent: false);
                hasProjection = await projectionEnumerator.MoveNextAsync();
            }
            else if (hasTrigger)
            {
                // Only trigger events remaining
                yield return (triggerEnumerator.Current, IsProjectionEvent: false, IsTriggerEvent: true);
                hasTrigger = await triggerEnumerator.MoveNextAsync();
            }
        }
    }
}

using Microsoft.Extensions.Logging;
using Rickten.EventStore;

namespace Rickten.Projector;

/// <summary>
/// Executes projections for building event-sourced read model views.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ProjectionRunner"/> class.
/// </remarks>
/// <param name="eventStore">The event store to load events from.</param>
/// <param name="projectionStore">The projection store for loading/saving checkpoints.</param>
/// <param name="logger">Optional logger for diagnostic information.</param>
public sealed class ProjectionRunner(IEventStore eventStore,
                                      IProjectionStore projectionStore,
                                      ILogger<ProjectionRunner>? logger = null)
{
    private readonly IProjectionStore _projectionStore  = projectionStore;
    private readonly IEventStore _eventStore            = eventStore;
    private readonly ILogger<ProjectionRunner>? _logger = logger;
    /// <summary>
    /// Rebuilds a projection from scratch starting at a specific global position.
    /// Does not save checkpoints - caller is responsible for persistence.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <param name="projection">The projection to apply.</param>
    /// <param name="fromGlobalPosition">The global position to start from (default: 0 = beginning).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projected view and the last processed global position.</returns>
    public async Task<(TView View, long LastGlobalPosition)> RebuildAsync<TView>(
        IProjection<TView> projection,
        long fromGlobalPosition = 0,
        CancellationToken cancellationToken = default)
    {
        var view = projection.InitialView();
        long lastGlobalPosition = fromGlobalPosition;

        // Load events with optional filtering
        await foreach (var streamEvent in _eventStore.LoadAllAsync(
            fromGlobalPosition,
            projection.AggregateTypeFilter,
            projection.EventTypeFilter,
            cancellationToken))
        {
            view = projection.Apply(view, streamEvent);
            lastGlobalPosition = streamEvent.GlobalPosition;
        }

        return (view, lastGlobalPosition);
    }

    /// <summary>
    /// Rebuilds a projection up to a specific global position (inclusive).
    /// Does not save checkpoints - caller is responsible for persistence.
    /// Useful for reactions that need projection state at a specific historical point.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <param name="projection">The projection to apply.</param>
    /// <param name="untilGlobalPosition">Process events up to and including this global position.</param>
    /// <param name="fromGlobalPosition">The global position to start from (default: 0 = beginning).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projected view and the last processed global position.</returns>
    public async Task<(TView View, long LastGlobalPosition)> RebuildUntilAsync<TView>(
        IProjection<TView> projection,
        long untilGlobalPosition,
        long fromGlobalPosition = 0,
        CancellationToken cancellationToken = default)
    {
        var view = projection.InitialView();
        long lastGlobalPosition = fromGlobalPosition;

        // Load events with optional filtering, stopping at untilGlobalPosition (inclusive)
        await foreach (var streamEvent in _eventStore.LoadAllAsync(
            fromGlobalPosition,
            projection.AggregateTypeFilter,
            projection.EventTypeFilter,
            cancellationToken))
        {
            view = projection.Apply(view, streamEvent);
            lastGlobalPosition = streamEvent.GlobalPosition;

            // Stop if we've reached the target position (inclusive)
            if (streamEvent.GlobalPosition >= untilGlobalPosition)
            {
                break;
            }
        }

        return (view, lastGlobalPosition);
    }

    /// <summary>
    /// Catches up a projection from its last checkpoint in the "system" namespace.
    /// Loads the checkpoint from the projection store, applies new events, and saves the updated view.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <param name="projection">The projection to apply.</param>
    /// <param name="projectionName">The name to use for storing the projection (defaults to projection's name if available).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current view and checkpoint global position.</returns>
    public async Task<(TView View, long GlobalPosition)> CatchUpAsync<TView>(
        IProjection<TView> projection,
        string? projectionName = null,
        CancellationToken cancellationToken = default)
    {
        return await CatchUpAsync(projection, projectionName, "system", cancellationToken);
    }

    /// <summary>
    /// Catches up a projection from its last checkpoint in a specific namespace.
    /// Loads the checkpoint from the projection store, applies new events, and saves the updated view.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <param name="projection">The projection to apply.</param>
    /// <param name="projectionName">The name to use for storing the projection (defaults to projection's name if available).</param>
    /// <param name="namespace">The namespace for the projection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current view and checkpoint global position.</returns>
    public async Task<(TView View, long GlobalPosition)> CatchUpAsync<TView>(
        IProjection<TView> projection,
        string? projectionName,
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        // Determine projection name
        var name = projectionName;
        if (string.IsNullOrEmpty(name))
        {
            name = projection.ProjectionName;
        }
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException(
                "Projection name must be provided either via parameter or [Projection] attribute",
                nameof(projectionName));
        }

        // Load last checkpoint
        var checkpoint = await _projectionStore.LoadProjectionAsync<TView>(name, @namespace, cancellationToken);
        var view = checkpoint is not null ? checkpoint.State : projection.InitialView();
        var fromGlobalPosition = checkpoint?.GlobalPosition ?? 0;

        // Process new events
        var lastGlobalPosition = fromGlobalPosition;
        await foreach (var streamEvent in _eventStore.LoadAllAsync(
            fromGlobalPosition,
            projection.AggregateTypeFilter,
            projection.EventTypeFilter,
            cancellationToken))
        {
            view = projection.Apply(view, streamEvent);
            lastGlobalPosition = streamEvent.GlobalPosition;
        }

        // Save updated checkpoint if we processed any events
        if (lastGlobalPosition > fromGlobalPosition)
        {
            await _projectionStore.SaveProjectionAsync(name, lastGlobalPosition, view, @namespace, cancellationToken);
        }

        return (view, lastGlobalPosition);
    }

    /// <summary>
    /// Catches up a projection from its last checkpoint up to a specific position (inclusive).
    /// Loads the checkpoint from the projection store, applies events up to the target position, and saves if progress was made.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <param name="projection">The projection to apply.</param>
    /// <param name="untilGlobalPosition">Process events up to and including this global position.</param>
    /// <param name="projectionName">The name to use for storing the projection (defaults to projection's name if available).</param>
    /// <param name="namespace">The namespace for the projection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projected view and the last processed global position.</returns>
    public async Task<(TView View, long GlobalPosition)> CatchUpUntilAsync<TView>(
        IProjection<TView> projection,
        long untilGlobalPosition,
        string? projectionName = null,
        string @namespace = "system",
        CancellationToken cancellationToken = default)
    {
        // Determine projection name
        var name = projectionName;
        if (string.IsNullOrEmpty(name))
        {
            name = projection.ProjectionName;
        }
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException(
                "Projection name must be provided either via parameter or [Projection] attribute",
                nameof(projectionName));
        }

        // Load last checkpoint
        var checkpoint = await _projectionStore.LoadProjectionAsync<TView>(name, @namespace, cancellationToken);
        var view = checkpoint is not null ? checkpoint.State : projection.InitialView();
        var fromGlobalPosition = checkpoint?.GlobalPosition ?? 0;

        // Process events up to target position
        var lastGlobalPosition = fromGlobalPosition;
        await foreach (var streamEvent in _eventStore.LoadAllAsync(
            fromGlobalPosition,
            projection.AggregateTypeFilter,
            projection.EventTypeFilter,
            cancellationToken))
        {
            view = projection.Apply(view, streamEvent);
            lastGlobalPosition = streamEvent.GlobalPosition;

            // Stop if we've reached the target position (inclusive)
            if (streamEvent.GlobalPosition >= untilGlobalPosition)
            {
                break;
            }
        }

        // Save updated checkpoint if we processed any events
        if (lastGlobalPosition > fromGlobalPosition)
        {
            await _projectionStore.SaveProjectionAsync(name, lastGlobalPosition, view, @namespace, cancellationToken);
        }

        return (view, lastGlobalPosition);
    }

    /// <summary>
    /// Synchronizes a projection view to a target position.
    /// If current position is ahead of target, rebuilds from scratch (can't go backwards).
    /// If current position is behind target, catches up from checkpoint.
    /// If equal, returns current view unchanged.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <param name="projection">The projection to apply.</param>
    /// <param name="currentView">The current projection view state.</param>
    /// <param name="currentPosition">The current projection position.</param>
    /// <param name="targetPosition">The target position to synchronize to.</param>
    /// <param name="projectionName">The name to use for loading the projection (defaults to projection's name if available).</param>
    /// <param name="namespace">The namespace for the projection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The synchronized view and position.</returns>
    public async Task<(TView View, long Position)> SyncToPositionAsync<TView>(
        IProjection<TView> projection,
        TView currentView,
        long currentPosition,
        long targetPosition,
        string? projectionName = null,
        string @namespace = "system",
        CancellationToken cancellationToken = default)
    {
        if (currentPosition > targetPosition)
        {
            // Projection ahead - must rebuild from scratch (can't go backwards)
            return await RebuildUntilAsync(
                projection,
                targetPosition,
                fromGlobalPosition: 0,
                cancellationToken);
        }

        if (currentPosition < targetPosition)
        {
            // Projection behind - catch up from checkpoint
            return await CatchUpUntilAsync(
                projection,
                targetPosition,
                projectionName,
                @namespace,
                cancellationToken);
        }

        // Equal - no action needed
        return (currentView, currentPosition);
    }
}

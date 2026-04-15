using Rickten.EventStore;

namespace Rickten.Projector;

/// <summary>
/// Utilities for projecting events into read model views.
/// </summary>
public static class ProjectionRunner
{
    /// <summary>
    /// Rebuilds a projection from scratch starting at a specific version.
    /// Does not save checkpoints - caller is responsible for persistence.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <param name="eventStore">The event store to load events from.</param>
    /// <param name="projection">The projection to apply.</param>
    /// <param name="fromVersion">The version to start from (default: 0 = beginning).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projected view and the last processed version.</returns>
    public static async Task<(TView View, long LastVersion)> RebuildAsync<TView>(
        IEventStore eventStore,
        IProjection<TView> projection,
        long fromVersion = 0,
        CancellationToken cancellationToken = default)
    {
        var view = projection.InitialView();
        long lastVersion = fromVersion;

        // Get filters from projection if it's a Projection<T>
        string[]? aggregateFilter = null;
        string[]? eventTypeFilter = null;

        if (projection is Projection<TView> proj)
        {
            aggregateFilter = proj.AggregateTypeFilter;
            eventTypeFilter = proj.EventTypeFilter;
        }

        // Load events with optional filtering
        await foreach (var streamEvent in eventStore.LoadAllAsync(
            fromVersion + 1,
            aggregateFilter,
            eventTypeFilter,
            cancellationToken))
        {
            view = projection.Apply(view, streamEvent);
            lastVersion = streamEvent.GlobalPosition;
        }

        return (view, lastVersion);
    }

    /// <summary>
    /// Catches up a projection from its last checkpoint.
    /// Loads the checkpoint from the projection store, applies new events, and saves the updated view.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <param name="eventStore">The event store to load events from.</param>
    /// <param name="projectionStore">The projection store for loading/saving checkpoints.</param>
    /// <param name="projection">The projection to apply.</param>
    /// <param name="projectionName">The name to use for storing the projection (defaults to projection's name if available).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current view and checkpoint version.</returns>
    public static async Task<(TView View, long Version)> CatchUpAsync<TView>(
        IEventStore eventStore,
        IProjectionStore projectionStore,
        IProjection<TView> projection,
        string? projectionName = null,
        CancellationToken cancellationToken = default)
    {
        // Determine projection name
        var name = projectionName;
        if (string.IsNullOrEmpty(name) && projection is Projection<TView> proj)
        {
            name = proj.ProjectionName;
        }
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException(
                "Projection name must be provided either via parameter or [Projection] attribute",
                nameof(projectionName));
        }

        // Load last checkpoint
        var checkpoint = await projectionStore.LoadProjectionAsync<TView>(name, cancellationToken);
        var view = checkpoint is not null ? checkpoint.State : projection.InitialView();
        var fromVersion = checkpoint?.GlobalPosition ?? 0;

        // Get filters
        string[]? aggregateFilter = null;
        string[]? eventTypeFilter = null;

        if (projection is Projection<TView> p)
        {
            aggregateFilter = p.AggregateTypeFilter;
            eventTypeFilter = p.EventTypeFilter;
        }

        // Process new events
        var lastVersion = fromVersion;
        await foreach (var streamEvent in eventStore.LoadAllAsync(
            fromVersion + 1,
            aggregateFilter,
            eventTypeFilter,
            cancellationToken))
        {
            view = projection.Apply(view, streamEvent);
            lastVersion = streamEvent.GlobalPosition;
        }

        // Save updated checkpoint if we processed any events
        if (lastVersion > fromVersion)
        {
            await projectionStore.SaveProjectionAsync(name, lastVersion, view, cancellationToken);
        }

        return (view, lastVersion);
    }
}

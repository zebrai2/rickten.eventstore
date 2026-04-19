using Rickten.EventStore;

namespace Rickten.Aggregator;

/// <summary>
/// Repository for loading and persisting aggregate state, following the DDD Repository pattern.
/// Handles event stream loading, state folding, event persistence, and snapshot management.
/// </summary>
/// <typeparam name="TState">The aggregate state type.</typeparam>
public interface IAggregateRepository<TState>
{
    /// <summary>
    /// Loads all events from a stream, validates ordering and completeness, and folds them into state.
    /// Uses the configured snapshot store to start from the latest snapshot to optimize loading.
    /// </summary>
    /// <param name="streamIdentifier">The stream to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current state and version.</returns>
    /// <exception cref="InvalidOperationException">Thrown when stream has gaps, ordering issues, or duplicate versions.</exception>
    Task<(TState State, long Version)> LoadStateAsync(
        StreamIdentifier streamIdentifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends events to the event store with optimistic concurrency control.
    /// </summary>
    /// <param name="expectedVersion">The expected current stream version for optimistic concurrency.</param>
    /// <param name="events">The events to append to the stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The appended events with their assigned stream pointers and global positions.</returns>
    /// <exception cref="StreamVersionConflictException">Thrown when the expected version does not match the actual stream version.</exception>
    Task<IReadOnlyList<StreamEvent>> AppendEventsAsync(
        StreamPointer expectedVersion,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies appended events to the current state and saves a snapshot if the snapshot interval 
    /// is configured and the final version is at an interval boundary.
    /// </summary>
    /// <param name="currentState">The state before applying the appended events.</param>
    /// <param name="appendedEvents">The events that were appended to the event store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new state after applying all events.</returns>
    Task<TState> SaveSnapshotIfNeededAsync(
        TState currentState,
        IReadOnlyList<StreamEvent> appendedEvents,
        CancellationToken cancellationToken = default);
}

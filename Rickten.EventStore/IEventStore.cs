namespace Rickten.EventStore;

/// <summary>
/// Represents an event store for persisting and retrieving event streams.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Loads events from a specific stream starting from the specified version.
    /// </summary>
    /// <param name="fromVersion">The stream pointer indicating where to start loading events.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>An async enumerable of stream events.</returns>
    IAsyncEnumerable<StreamEvent> LoadAsync(
        StreamPointer fromVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all events across all streams from a global position.
    /// </summary>
    /// <param name="fromGlobalPosition">The global position to start loading from. Defaults to 0 (beginning).</param>
    /// <param name="streamTypeFilter">Optional filter to include only specific stream types.</param>
    /// <param name="eventsFilter">Optional filter to include only specific event types.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>An async enumerable of stream events from all matching streams.</returns>
    IAsyncEnumerable<StreamEvent> LoadAllAsync(
        long fromGlobalPosition = 0,
        string[]? streamTypeFilter = null,
        string[]? eventsFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends events to a stream with optimistic concurrency control.
    /// </summary>
    /// <param name="expectedVersion">
    /// The expected current stream version for optimistic concurrency. 
    /// Version 0 indicates a new stream (no events written yet).
    /// If the stream has 5 events, pass version 5 to append after the last event.
    /// The new events will be written starting at version + 1.
    /// </param>
    /// <param name="events">The events to append to the stream.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The appended events with their assigned stream pointers and global positions.</returns>
    /// <exception cref="StreamVersionConflictException">Thrown when the expected version does not match the actual stream version.</exception>
    Task<IReadOnlyList<StreamEvent>> AppendAsync(
        StreamPointer expectedVersion,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default);
}

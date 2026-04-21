namespace Rickten.EventStore;

/// <summary>
/// Represents an event store for persisting and retrieving event streams.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Loads events from a specific stream starting after the specified version.
    /// Events are loaded exclusively - if fromVersion.Version is N, events with version &gt; N are returned.
    /// To load all events from the beginning, pass a StreamPointer with version 0.
    /// </summary>
    /// <param name="fromVersion">The stream pointer indicating the version to start loading after (exclusive).</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>An async enumerable of stream events.</returns>
    IAsyncEnumerable<StreamEvent> LoadAsync(
        StreamPointer fromVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all events across all streams starting after a global position.
    /// Events are loaded exclusively - if fromGlobalPosition is N, events with global position &gt; N are returned.
    /// To load all events from the beginning, pass 0 (default).
    /// </summary>
    /// <param name="fromGlobalPosition">The global position to start loading after (exclusive). Defaults to 0 (beginning).</param>
    /// <param name="streamTypeFilter">Optional filter to include only specific stream types.</param>
    /// <param name="eventsFilter">Optional filter to include only specific event types.</param>
    /// <param name="untilGlobalPosition">Optional upper bound (inclusive). Only events with global position &lt;= this value are returned. Null means no upper limit.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>An async enumerable of stream events from all matching streams.</returns>
    IAsyncEnumerable<StreamEvent> LoadAllAsync(
        long fromGlobalPosition = 0,
        string[]? streamTypeFilter = null,
        string[]? eventsFilter = null,
        long? untilGlobalPosition = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads events from multiple filter configurations and merges them by global position.
    /// When the same event matches multiple filters, it is yielded once with all matching filter indices.
    /// </summary>
    /// <param name="fromGlobalPosition">The global position to start loading after (exclusive).</param>
    /// <param name="filters">Array of filter configurations (streamTypeFilter, eventsFilter). Each tuple represents a separate query.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>An async enumerable of tuples containing the event and array of matching filter indices.</returns>
    IAsyncEnumerable<(StreamEvent Event, int[] MatchingFilters)> LoadAllMergedAsync(
        long fromGlobalPosition,
        (string[]? streamTypeFilter, string[]? eventsFilter)[] filters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current version of a stream without loading all events.
    /// Returns a StreamPointer with version 0 if the stream does not exist.
    /// </summary>
    /// <param name="streamIdentifier">The stream identifier to get the current version for.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The current stream pointer (stream identifier + version).</returns>
    Task<StreamPointer> GetCurrentVersionAsync(
        StreamIdentifier streamIdentifier,
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

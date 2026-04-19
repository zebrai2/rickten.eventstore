using Rickten.EventStore;

namespace Rickten.Aggregator;

/// <summary>
/// Repository for loading and persisting aggregate state, following the DDD Repository pattern.
/// Handles event stream loading, state folding, event persistence, and snapshot management.
/// </summary>
/// <typeparam name="TState">The aggregate state type.</typeparam>
/// <remarks>
/// Initializes a new instance of the <see cref="AggregateRepository{TState}"/> class.
/// </remarks>
/// <param name="eventStore">The event store.</param>
/// <param name="folder">The state folder for this aggregate type.</param>
/// <param name="snapshotStore">Snapshot store for loading and saving snapshots.</param>
public class AggregateRepository<TState>(
    IEventStore eventStore,
    IStateFolder<TState> folder,
    ISnapshotStore snapshotStore) : IAggregateRepository<TState>
{
    private readonly IEventStore _eventStore        = eventStore    ?? throw new ArgumentNullException(nameof(eventStore));
    private readonly IStateFolder<TState> _folder   = folder        ?? throw new ArgumentNullException(nameof(folder));
    private readonly ISnapshotStore _snapshotStore  = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));

    /// <summary>
    /// Loads all events from a stream, validates ordering and completeness, and folds them into state.
    /// Uses the configured snapshot store to start from the latest snapshot to optimize loading.
    /// </summary>
    /// <param name="streamIdentifier">The stream to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current state and stream pointer.</returns>
    /// <exception cref="InvalidOperationException">Thrown when stream has gaps, ordering issues, or duplicate versions.</exception>
    public async Task<(TState State, StreamPointer Pointer)> LoadStateAsync(
        StreamIdentifier streamIdentifier,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotStore.LoadSnapshotAsync(streamIdentifier, cancellationToken);
        var version  = snapshot?.StreamPointer.Version ?? 0;
        var pointer  = streamIdentifier.At(version);

        var state = (snapshot != null && snapshot.State is TState snapshotState)
            ? snapshotState
            : _folder.InitialState();


        await foreach (var streamEvent in _eventStore.LoadAsync(pointer, cancellationToken))
        {
            var expectedVersion = ++version;

            ValidateStreamType(streamIdentifier, streamEvent);
            ValidateStreamVersion(streamIdentifier, streamEvent, expectedVersion);
            ValidateEvent(streamIdentifier, streamEvent);

            state = _folder.Apply(state, streamEvent.Event);
            pointer = streamEvent.StreamPointer;
            version = streamEvent.StreamPointer.Version;
        }

        return (state, pointer);
    }

    private static void ValidateEvent(StreamIdentifier streamIdentifier, StreamEvent streamEvent)
    {
        if (streamEvent.Event == null)
        {
            throw new InvalidOperationException(
                $"Null event found in stream {streamIdentifier.StreamType}/{streamIdentifier.Identifier} at version {streamEvent.StreamPointer.Version}");
        }
    }

    private static void ValidateStreamVersion(StreamIdentifier streamIdentifier, StreamEvent streamEvent, long expectedVersion)
    {
        if (streamEvent.StreamPointer != expectedVersion)
        {
            if (streamEvent.StreamPointer < expectedVersion)
            {
                throw new InvalidOperationException(
                    $"Duplicate or out-of-order event in stream {streamIdentifier.StreamType}/{streamIdentifier.Identifier}. " +
                    $"Expected version {expectedVersion}, got {streamEvent.StreamPointer.Version}");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Gap in stream {streamIdentifier.StreamType}/{streamIdentifier.Identifier}. " +
                    $"Expected version {expectedVersion}, got {streamEvent.StreamPointer.Version}. " +
                    $"Missing versions: {string.Join(", ", Enumerable.Range((int)expectedVersion, (int)(streamEvent.StreamPointer.Version - expectedVersion)))}");
            }
        }
    }

    private static void ValidateStreamType(StreamIdentifier streamIdentifier, StreamEvent streamEvent)
    {
        if (streamEvent.StreamPointer.Stream != streamIdentifier)
        {
            throw new InvalidOperationException(
                $"Stream identifier mismatch. Expected {streamIdentifier.StreamType}/{streamIdentifier.Identifier}, " +
                $"got {streamEvent.StreamPointer.Stream.StreamType}/{streamEvent.StreamPointer.Stream.Identifier}");
        }
    }

    /// <summary>
    /// Appends events to the event store with optimistic concurrency control.
    /// </summary>
    /// <param name="expectedVersion">The expected current stream version for optimistic concurrency.</param>
    /// <param name="events">The events to append to the stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The appended events with their assigned stream pointers and global positions.</returns>
    /// <exception cref="StreamVersionConflictException">Thrown when the expected version does not match the actual stream version.</exception>
    public async Task<IReadOnlyList<StreamEvent>> AppendEventsAsync(
        StreamPointer expectedVersion,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default)
    {
        return await _eventStore.AppendAsync(expectedVersion, events, cancellationToken);
    }

    /// <summary>
    /// Validates that raw events can be successfully folded into state before persisting them.
    /// This is a pre-append validation to ensure events won't corrupt the stream.
    /// If validation fails, no events are persisted.
    /// </summary>
    /// <param name="currentState">The state before applying the events.</param>
    /// <param name="events">The raw events to validate (before they're wrapped in StreamEvent).</param>
    /// <returns>The new state after applying events (validation successful).</returns>
    /// <exception cref="InvalidOperationException">Thrown when event folding fails (EnsureValid, bad When handler, etc.).</exception>
    public TState ValidateFold(
        TState currentState,
        IReadOnlyList<object> events)
    {
        var newState = currentState;
        foreach (var evt in events)
        {
            newState = _folder.Apply(newState, evt);
        }
        return newState;
    }

    /// <summary>
    /// Saves a snapshot if the snapshot interval is configured and we crossed or reached an interval boundary.
    /// </summary>
    /// <param name="newState">The state to snapshot.</param>
    /// <param name="previousVersion">The version before appending events.</param>
    /// <param name="finalVersion">The stream pointer after appending events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveSnapshotIfNeededAsync(
        TState newState,
        long previousVersion,
        StreamPointer finalVersion,
        CancellationToken cancellationToken = default)
    {
        if (_folder is not StateFolder<TState> stateFolder)
            return;

        var snapshotInterval = stateFolder.SnapshotInterval;
        if (snapshotInterval <= 0)
            return;

        // Check if we crossed or landed on an interval boundary
        // Example: interval=100, went from v99 to v201
        // Previous boundary: 99/100 = 0, Current boundary: 201/100 = 2
        // We crossed boundaries (0 != 2), so we should snapshot
        var previousBoundary = previousVersion / snapshotInterval;
        var currentBoundary = finalVersion.Version / snapshotInterval;

        if (currentBoundary > previousBoundary)
        {
            await _snapshotStore.SaveSnapshotAsync(finalVersion, newState!, cancellationToken);
        }
    }
}

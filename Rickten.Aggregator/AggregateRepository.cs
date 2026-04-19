using Rickten.EventStore;

namespace Rickten.Aggregator;

/// <summary>
/// Repository for loading and persisting aggregate state, following the DDD Repository pattern.
/// Handles event stream loading, state folding, event persistence, and snapshot management.
/// </summary>
/// <typeparam name="TState">The aggregate state type.</typeparam>
public class AggregateRepository<TState> : IAggregateRepository<TState>
{
    private readonly IEventStore _eventStore;
    private readonly IStateFolder<TState> _folder;
    private readonly ISnapshotStore? _snapshotStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRepository{TState}"/> class.
    /// </summary>
    /// <param name="eventStore">The event store.</param>
    /// <param name="folder">The state folder for this aggregate type.</param>
    /// <param name="snapshotStore">Optional snapshot store for loading and saving snapshots.</param>
    public AggregateRepository(
        IEventStore eventStore,
        IStateFolder<TState> folder,
        ISnapshotStore? snapshotStore = null)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
        _snapshotStore = snapshotStore;
    }

    /// <summary>
    /// Loads all events from a stream, validates ordering and completeness, and folds them into state.
    /// Uses the configured snapshot store to start from the latest snapshot to optimize loading.
    /// </summary>
    /// <param name="streamIdentifier">The stream to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current state and version.</returns>
    /// <exception cref="InvalidOperationException">Thrown when stream has gaps, ordering issues, or duplicate versions.</exception>
    public async Task<(TState State, long Version)> LoadStateAsync(
        StreamIdentifier streamIdentifier,
        CancellationToken cancellationToken = default)
    {
        // Determine starting state and version
        TState state;
        long version;

        if (_snapshotStore != null)
        {
            var snapshot = await _snapshotStore.LoadSnapshotAsync(streamIdentifier, cancellationToken);
            if (snapshot != null && snapshot.State is TState snapshotState)
            {
                state = snapshotState;
                version = snapshot.StreamPointer.Version;
            }
            else
            {
                state = _folder.InitialState();
                version = 0;
            }
        }
        else
        {
            state = _folder.InitialState();
            version = 0;
        }

        // Use implicit cast from StreamIdentifier to StreamPointer (version 0), then MoveTo starting version
        var pointer = ((StreamPointer)streamIdentifier).WithVersion(version);

        await foreach (var streamEvent in _eventStore.LoadAsync(pointer, cancellationToken))
        {
            // Validate stream identifier matches
            if (streamEvent.StreamPointer.Stream != streamIdentifier)
            {
                throw new InvalidOperationException(
                    $"Stream identifier mismatch. Expected {streamIdentifier.StreamType}/{streamIdentifier.Identifier}, " +
                    $"got {streamEvent.StreamPointer.Stream.StreamType}/{streamEvent.StreamPointer.Stream.Identifier}");
            }

            // Validate version ordering (events are 1-indexed)
            // The next event must be exactly version + 1
            var expectedVersion = version + 1;
            if (streamEvent.StreamPointer.Version != expectedVersion)
            {
                if (streamEvent.StreamPointer.Version < expectedVersion)
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

            // Validate event is not null
            if (streamEvent.Event == null)
            {
                throw new InvalidOperationException(
                    $"Null event found in stream {streamIdentifier.StreamType}/{streamIdentifier.Identifier} at version {streamEvent.StreamPointer.Version}");
            }

            // Apply event to state
            state = _folder.Apply(state, streamEvent.Event);
            version = streamEvent.StreamPointer.Version;
        }

        return (state, version);
    }

    /// <summary>
    /// Internal helper for applying a list of events to state.
    /// Used by LoadStateAsync and ApplyEvents (public method).
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="events">The events to apply.</param>
    /// <returns>The new state after applying all events.</returns>
    private TState ApplyEventsInternal(TState state, IReadOnlyList<object> events)
    {
        var newState = state;
        foreach (var evt in events)
        {
            newState = _folder.Apply(newState, evt);
        }
        return newState;
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
        return ApplyEventsInternal(currentState, events);
    }

    /// <summary>
    /// Applies appended events to the current state by folding them through the state folder.
    /// This produces the new state after the events have been applied.
    /// </summary>
    /// <param name="currentState">The state before applying the events.</param>
    /// <param name="appendedEvents">The events to apply.</param>
    /// <returns>The new state after applying all events.</returns>
    public TState ApplyEvents(
        TState currentState,
        IReadOnlyList<StreamEvent> appendedEvents)
    {
        if (appendedEvents.Count == 0)
        {
            return currentState;
        }

        // Extract events from StreamEvents and apply to state
        var events = appendedEvents.Select(e => e.Event).ToList();
        return ApplyEventsInternal(currentState, events);
    }

    /// <summary>
    /// Saves a snapshot if the snapshot interval is configured and the final version
    /// is at an interval boundary.
    /// </summary>
    /// <param name="newState">The state to snapshot.</param>
    /// <param name="finalVersion">The stream pointer at which to save the snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveSnapshotIfNeededAsync(
        TState newState,
        StreamPointer finalVersion,
        CancellationToken cancellationToken = default)
    {
        // Save snapshot if at interval boundary
        if (_snapshotStore != null && _folder is StateFolder<TState> stateFolder)
        {
            var snapshotInterval = stateFolder.SnapshotInterval;
            if (snapshotInterval > 0 && finalVersion.Version % snapshotInterval == 0)
            {
                await _snapshotStore.SaveSnapshotAsync(finalVersion, newState!, cancellationToken);
            }
        }
    }
}

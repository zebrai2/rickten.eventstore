using Rickten.EventStore;

namespace Rickten.Aggregator;

/// <summary>
/// Utilities for loading and folding event streams into aggregate state.
/// </summary>
public static class StateRunner
{
    /// <summary>
    /// Loads all events from a stream, validates ordering and completeness, and folds them into state.
    /// If a snapshot store is provided, starts from the latest snapshot to optimize loading.
    /// </summary>
    /// <typeparam name="TState">The aggregate state type.</typeparam>
    /// <param name="eventStore">The event store.</param>
    /// <param name="folder">The state folder.</param>
    /// <param name="streamIdentifier">The stream to load.</param>
    /// <param name="snapshotStore">Optional snapshot store to load from a snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current state and version.</returns>
    /// <exception cref="InvalidOperationException">Thrown when stream has gaps, ordering issues, or duplicate versions.</exception>
    public static async Task<(TState State, long Version)> LoadStateAsync<TState>(
        IEventStore eventStore,
        IStateFolder<TState> folder,
        StreamIdentifier streamIdentifier,
        ISnapshotStore? snapshotStore = null,
        CancellationToken cancellationToken = default)
    {
        TState state;
        long version;
        long expectedVersion;

        // Try to load from snapshot first
        if (snapshotStore != null)
        {
            var snapshot = await snapshotStore.LoadSnapshotAsync(streamIdentifier, cancellationToken);
            if (snapshot != null && snapshot.State is TState snapshotState)
            {
                state = snapshotState;
                version = snapshot.StreamPointer.Version;
                expectedVersion = version + 1;
            }
            else
            {
                // No snapshot found, start from beginning
                state = folder.InitialState();
                version = 0;
                expectedVersion = 1;
            }
        }
        else
        {
            // No snapshot store provided, start from beginning
            state = folder.InitialState();
            version = 0;
            expectedVersion = 1;
        }

        var pointer = new StreamPointer(streamIdentifier, version);

        await foreach (var streamEvent in eventStore.LoadAsync(pointer, cancellationToken))
        {
            // Validate stream identifier matches
            if (streamEvent.StreamPointer.Stream != streamIdentifier)
            {
                throw new InvalidOperationException(
                    $"Stream identifier mismatch. Expected {streamIdentifier.StreamType}/{streamIdentifier.Identifier}, " +
                    $"got {streamEvent.StreamPointer.Stream.StreamType}/{streamEvent.StreamPointer.Stream.Identifier}");
            }

            // Validate version ordering (events are 1-indexed)
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
            state = folder.Apply(state, streamEvent.Event);
            version = streamEvent.StreamPointer.Version;
            expectedVersion = version + 1;
        }

        return (state, version);
    }

    /// <summary>
    /// Executes a command and returns events to append.
    /// If the folder has SnapshotInterval > 0, automatically saves snapshots at the configured interval.
    /// </summary>
    /// <typeparam name="TState">The aggregate state type.</typeparam>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="eventStore">The event store.</param>
    /// <param name="folder">The state folder.</param>
    /// <param name="decider">The command decider.</param>
    /// <param name="streamIdentifier">The stream identifier.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="snapshotStore">Optional snapshot store for automatic snapshots.</param>
    /// <param name="metadata">Optional metadata to attach to events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new state, version, and appended events.</returns>
    public static async Task<(TState State, long Version, IReadOnlyList<StreamEvent> Events)> ExecuteAsync<TState, TCommand>(
        IEventStore eventStore,
        IStateFolder<TState> folder,
        ICommandDecider<TState, TCommand> decider,
        StreamIdentifier streamIdentifier,
        TCommand command,
        ISnapshotStore? snapshotStore = null,
        IReadOnlyList<AppendMetadata>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        // Load current state (with snapshot optimization if available)
        var (state, currentVersion) = await LoadStateAsync(eventStore, folder, streamIdentifier, snapshotStore, cancellationToken);

        // Execute command to get events
        var events = decider.Execute(state, command);

        if (events.Count == 0)
        {
            // No events to append (idempotent)
            return (state, currentVersion, []);
        }

        // Append events to store with current version
        var pointer = new StreamPointer(streamIdentifier, currentVersion);
        var appendEvents = events.Select(e => new AppendEvent(e, metadata)).ToList();
        var appendedEvents = await eventStore.AppendAsync(pointer, appendEvents, cancellationToken);

        // Fold events into state
        var newState = state;
        foreach (var streamEvent in appendedEvents)
        {
            newState = folder.Apply(newState, streamEvent.Event);
        }

        var newVersion = appendedEvents.Last().StreamPointer.Version;

        // Auto-snapshot if configured
        if (snapshotStore != null && folder is StateFolder<TState> stateFolder)
        {
            var snapshotInterval = stateFolder.SnapshotInterval;
            if (snapshotInterval > 0 && newVersion % snapshotInterval == 0)
            {
                var snapshotPointer = new StreamPointer(streamIdentifier, newVersion);
                await snapshotStore.SaveSnapshotAsync(snapshotPointer, newState!, cancellationToken);
            }
        }

        return (newState, newVersion, appendedEvents);
    }
}

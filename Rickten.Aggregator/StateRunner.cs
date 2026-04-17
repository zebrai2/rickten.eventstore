using Rickten.EventStore;
using System.Reflection;

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
        // Determine starting state and version
        TState state;
        long version;

        if (snapshotStore != null)
        {
            var snapshot = await snapshotStore.LoadSnapshotAsync(streamIdentifier, cancellationToken);
            if (snapshot != null && snapshot.State is TState snapshotState)
            {
                state = snapshotState;
                version = snapshot.StreamPointer.Version;
            }
            else
            {
                state = folder.InitialState();
                version = 0;
            }
        }
        else
        {
            state = folder.InitialState();
            version = 0;
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
            state = folder.Apply(state, streamEvent.Event);
            version = streamEvent.StreamPointer.Version;
        }

        return (state, version);
    }

    /// <summary>
    /// Executes a command against the current aggregate state.
    /// If the folder has SnapshotInterval > 0, automatically saves snapshots at the configured interval.
    /// For commands with VersionMode = ExpectedVersion, validates the stream version before execution.
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
        // Determine version mode and expected version from command
        var commandType = command!.GetType();
        var commandAttr = commandType.GetCustomAttribute<CommandAttribute>();
        var versionMode = commandAttr?.VersionMode ?? CommandVersionMode.LatestVersion;

        long? expectedVersion = null;

        if (versionMode == CommandVersionMode.ExpectedVersion)
        {
            // Command requires expected version
            if (command is IExpectedVersionCommand expectedVersionCommand)
            {
                expectedVersion = expectedVersionCommand.ExpectedVersion;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Command '{commandType.Name}' has VersionMode = ExpectedVersion but does not implement IExpectedVersionCommand. " +
                    $"Either add 'long ExpectedVersion' property and implement IExpectedVersionCommand, or change VersionMode to LatestVersion.");
            }
        }

        return await ExecuteCoreAsync(
            eventStore,
            folder,
            decider,
            streamIdentifier,
            command,
            expectedVersion,
            snapshotStore,
            metadata,
            cancellationToken);
    }

    /// <summary>
    /// Executes a command against a specific expected stream version for optimistic concurrency control.
    /// Use this when you want to explicitly provide the expected version separately from the command.
    /// Throws StreamVersionConflictException if the current stream version does not match the expected version.
    /// </summary>
    /// <typeparam name="TState">The aggregate state type.</typeparam>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="eventStore">The event store.</param>
    /// <param name="folder">The state folder.</param>
    /// <param name="decider">The command decider.</param>
    /// <param name="streamIdentifier">The stream identifier.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="expectedVersion">The expected stream version.</param>
    /// <param name="snapshotStore">Optional snapshot store for automatic snapshots.</param>
    /// <param name="metadata">Optional metadata to attach to events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new state, version, and appended events.</returns>
    public static Task<(TState State, long Version, IReadOnlyList<StreamEvent> Events)> ExecuteAtVersionAsync<TState, TCommand>(
        IEventStore eventStore,
        IStateFolder<TState> folder,
        ICommandDecider<TState, TCommand> decider,
        StreamIdentifier streamIdentifier,
        TCommand command,
        long expectedVersion,
        ISnapshotStore? snapshotStore = null,
        IReadOnlyList<AppendMetadata>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCoreAsync(
            eventStore,
            folder,
            decider,
            streamIdentifier,
            command,
            expectedVersion,
            snapshotStore,
            metadata,
            cancellationToken);
    }

    private static async Task<(TState State, long Version, IReadOnlyList<StreamEvent> Events)> ExecuteCoreAsync<TState, TCommand>(
        IEventStore eventStore,
        IStateFolder<TState> folder,
        ICommandDecider<TState, TCommand> decider,
        StreamIdentifier streamIdentifier,
        TCommand command,
        long? expectedVersion,
        ISnapshotStore? snapshotStore,
        IReadOnlyList<AppendMetadata>? metadata,
        CancellationToken cancellationToken)
    {
        // Load current state (with snapshot optimization if available)
        var (state, currentVersion) = await LoadStateAsync(eventStore, folder, streamIdentifier, snapshotStore, cancellationToken);

        // If expected version is specified, validate it matches current version before deciding
        if (expectedVersion.HasValue && currentVersion != expectedVersion.Value)
        {
            throw new StreamVersionConflictException(
                new StreamPointer(streamIdentifier, expectedVersion.Value),
                new StreamPointer(streamIdentifier, currentVersion),
                $"Stream version conflict: expected version {expectedVersion.Value}, but current version is {currentVersion}. " +
                $"The stream has changed since the decision was made.");
        }

        // Execute command to get events
        var events = decider.Execute(state, command);

        if (events.Count == 0)
        {
            // No events to append (idempotent)
            return (state, currentVersion, []);
        }

        // Append events to store with current version (or expected version if specified)
        var appendVersion = expectedVersion ?? currentVersion;
        var pointer = new StreamPointer(streamIdentifier, appendVersion);
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

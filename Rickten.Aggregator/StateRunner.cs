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
    /// For commands with ExpectedVersionKey, validates the stream version from metadata before execution.
    /// </summary>
    /// <typeparam name="TState">The aggregate state type.</typeparam>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="eventStore">The event store.</param>
    /// <param name="folder">The state folder.</param>
    /// <param name="decider">The command decider.</param>
    /// <param name="streamIdentifier">The stream identifier.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="registry">The type metadata registry for command metadata lookup.</param>
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
        EventStore.TypeMetadata.ITypeMetadataRegistry registry,
        ISnapshotStore? snapshotStore = null,
        IReadOnlyList<AppendMetadata>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        // Extract expected version from metadata if command declares ExpectedVersionKey
        var (expectedVersion, expectedVersionKey) = GetExpectedVersionFromMetadata(command, metadata, registry);

        return await ExecuteCoreAsync(
            eventStore,
            folder,
            decider,
            streamIdentifier,
            command,
            expectedVersion,
            expectedVersionKey,
            snapshotStore,
            metadata,
            cancellationToken);
    }

    /// <summary>
    /// Extracts expected version from metadata if the command has an ExpectedVersionKey.
    /// Returns both the expected version and the key name so it can be filtered from persisted metadata.
    /// </summary>
    private static (long? expectedVersion, string? expectedVersionKey) GetExpectedVersionFromMetadata<TCommand>(
        TCommand command,
        IReadOnlyList<AppendMetadata>? metadata,
        EventStore.TypeMetadata.ITypeMetadataRegistry registry)
    {
        var commandType = command?.GetType();
        if (commandType == null)
        {
            return (null, null);
        }

        // Get command metadata from registry
        var typeMetadata = registry.GetMetadataByType(commandType);

        // CRITICAL: All commands must be registered in the registry
        // This is a configuration error that must be fixed immediately
        if (typeMetadata?.AttributeInstance is not CommandAttribute)
        {
            throw new InvalidOperationException(
                $"CRITICAL CONFIGURATION ERROR: Command type '{commandType.Name}' is not registered in the type metadata registry. " +
                $"This is a fatal setup error that must be fixed immediately. " +
                $"All command types must have a [Command] attribute and be registered via AddEventStore during service configuration. " +
                $"Ensure the assembly containing '{commandType.Name}' is included in the AddEventStore call. " +
                $"This error prevents command execution to avoid bypassing expected version validation.");
        }

        var commandAttr = (CommandAttribute)typeMetadata.AttributeInstance;
        var expectedVersionKey = commandAttr.ExpectedVersionKey;

        // If no ExpectedVersionKey is specified, return null (latest-version behavior)
        if (string.IsNullOrWhiteSpace(expectedVersionKey))
        {
            return (null, null);
        }

        // Find metadata item with matching key
        var metadataItem = metadata?.FirstOrDefault(m =>
            string.Equals(m.Key, expectedVersionKey, StringComparison.Ordinal));

        if (metadataItem == null)
        {
            throw new InvalidOperationException(
                $"Command '{commandType.Name}' requires expected version metadata key '{expectedVersionKey}', but it was not provided.");
        }

        var metadataValue = metadataItem.Value;

        if (metadataValue == null)
        {
            throw new InvalidOperationException(
                $"Command '{commandType.Name}' requires expected version metadata key '{expectedVersionKey}', but the value was null.");
        }

        // Convert to long - support long, int, and parseable strings
        var version = metadataValue switch
        {
            long longValue => longValue,
            int intValue => intValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            string stringValue when long.TryParse(stringValue, out var parsedValue) => parsedValue,
            _ => throw new InvalidOperationException(
                $"Command '{commandType.Name}' expected version metadata key '{expectedVersionKey}' has value of type '{metadataValue.GetType().Name}' which cannot be converted to long.")
        };

        return (version, expectedVersionKey);
    }



    private static async Task<(TState State, long Version, IReadOnlyList<StreamEvent> Events)> ExecuteCoreAsync<TState, TCommand>(
        IEventStore eventStore,
        IStateFolder<TState> folder,
        ICommandDecider<TState, TCommand> decider,
        StreamIdentifier streamIdentifier,
        TCommand command,
        long? expectedVersion,
        string? expectedVersionKey,
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

        // Filter expected version key from metadata before persisting
        // Expected version is request/decision context, not event data
        var filteredMetadata = metadata;
        if (!string.IsNullOrWhiteSpace(expectedVersionKey) && metadata != null)
        {
            filteredMetadata = metadata
                .Where(m => !string.Equals(m.Key, expectedVersionKey, StringComparison.Ordinal))
                .ToList();
        }

        // Append events to store with current version (or expected version if specified)
        var appendVersion = expectedVersion ?? currentVersion;
        var pointer = new StreamPointer(streamIdentifier, appendVersion);
        var appendEvents = events.Select(e => new AppendEvent(e, filteredMetadata)).ToList();
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

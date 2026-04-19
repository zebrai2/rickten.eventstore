using Rickten.EventStore;

namespace Rickten.Aggregator;

/// <summary>
/// Orchestrates the full command execution workflow: load state, execute command, append events, and optionally save snapshots.
/// This class provides the complete command execution logic including expected version validation.
/// State management (loading, folding, event persistence, snapshotting) is delegated to AggregateRepository.
/// </summary>
/// <typeparam name="TState">The aggregate state type.</typeparam>
/// <typeparam name="TCommand">The command type.</typeparam>
public class AggregateCommandExecutor<TState, TCommand>
{
    private readonly IAggregateRepository<TState> _repository;
    private readonly ICommandDecider<TState, TCommand> _decider;
    private readonly EventStore.TypeMetadata.ITypeMetadataRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateCommandExecutor{TState, TCommand}"/> class.
    /// </summary>
    /// <param name="repository">The aggregate repository for loading state, persisting events, and managing snapshots.</param>
    /// <param name="decider">The command decider for this command type.</param>
    /// <param name="registry">The type metadata registry for command metadata lookup.</param>
    public AggregateCommandExecutor(
        IAggregateRepository<TState> repository,
        ICommandDecider<TState, TCommand> decider,
        EventStore.TypeMetadata.ITypeMetadataRegistry registry)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _decider = decider ?? throw new ArgumentNullException(nameof(decider));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Executes a command against an aggregate stream.
    /// Loads current state, executes the command, appends events, and optionally saves snapshots.
    /// </summary>
    /// <param name="streamIdentifier">The stream identifier.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="metadata">Optional metadata to attach to events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new state, version, and appended events.</returns>
    public async Task<(TState State, long Version, IReadOnlyList<StreamEvent> Events)> ExecuteAsync(
        StreamIdentifier streamIdentifier,
        TCommand command,
        IReadOnlyList<AppendMetadata>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Load current state (with snapshot optimization if available)
        var (state, currentVersion) = await _repository.LoadStateAsync(streamIdentifier, cancellationToken);

        // Step 2: Execute command to get events
        var (events, expectedVersionKey) = ExecuteCommand(streamIdentifier, state, currentVersion, command, metadata);

        if (events.Count == 0)
        {
            // No events to append (idempotent command)
            return (state, currentVersion, Array.Empty<StreamEvent>());
        }

        // Step 3: Filter expected version key from metadata before persisting
        // Expected version is request/decision context, not event data
        var filteredMetadata = metadata;
        if (!string.IsNullOrWhiteSpace(expectedVersionKey) && metadata != null)
        {
            filteredMetadata = metadata
                .Where(m => !string.Equals(m.Key, expectedVersionKey, StringComparison.Ordinal))
                .ToList();
        }

        // Step 4: Pre-append validation - fold events to ensure they won't corrupt the stream
        // If this throws (EnsureValid, bad When handler, etc.), no events are persisted
        var newState = _repository.ValidateFold(state, events);

        // Step 5: Append events to store (persist after validation)
        // Use implicit cast from StreamIdentifier to StreamPointer (version 0), then WithVersion to current version
        var pointer = ((StreamPointer)streamIdentifier).WithVersion(currentVersion);
        var appendEvents = events.Select(e => new AppendEvent(e, filteredMetadata)).ToList();
        var appendedEvents = await _repository.AppendEventsAsync(pointer, appendEvents, cancellationToken);

        // Step 6: Save snapshot if at interval boundary
        var finalVersion = appendedEvents.Last().StreamPointer;
        await _repository.SaveSnapshotIfNeededAsync(newState, finalVersion, cancellationToken);

        return (newState, finalVersion.Version, appendedEvents);
    }

    /// <summary>
    /// Executes a command against the provided aggregate state.
    /// This method is pure - it does not load or save anything.
    /// For commands with ExpectedVersionKey, validates the current version from metadata before execution.
    /// </summary>
    /// <param name="streamIdentifier">The stream identifier (for exception messages).</param>
    /// <param name="state">The current aggregate state.</param>
    /// <param name="currentVersion">The current version of the aggregate.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="metadata">Optional metadata to attach to events.</param>
    /// <returns>The list of events to append and the expected version key (if any) to filter from persisted metadata.</returns>
    private (IReadOnlyList<object> Events, string? ExpectedVersionKey) ExecuteCommand(
        StreamIdentifier streamIdentifier,
        TState state,
        long currentVersion,
        TCommand command,
        IReadOnlyList<AppendMetadata>? metadata = null)
    {
        // Extract expected version from metadata if command declares ExpectedVersionKey
        var (expectedVersion, expectedVersionKey) = GetExpectedVersionFromMetadata(command, metadata);

        // If expected version is specified, validate it matches current version before deciding
        if (expectedVersion.HasValue && currentVersion != expectedVersion.Value)
        {
            // Use implicit cast + MoveTo for cleaner StreamPointer construction
            var expectedPointer = ((StreamPointer)streamIdentifier).WithVersion(expectedVersion.Value);
            var actualPointer = ((StreamPointer)streamIdentifier).WithVersion(currentVersion);
            throw new StreamVersionConflictException(
                expectedPointer,
                actualPointer,
                $"Version mismatch: expected version {expectedVersion.Value}, but current version is {currentVersion}. " +
                $"The aggregate has changed since the decision was made.");
        }

        // Execute command to get events
        var events = _decider.Execute(state, command);

        return (events, expectedVersionKey);
    }

    /// <summary>
    /// Extracts expected version from metadata if the command has an ExpectedVersionKey.
    /// Returns both the expected version and the key name so it can be filtered from persisted metadata.
    /// </summary>
    private (long? expectedVersion, string? expectedVersionKey) GetExpectedVersionFromMetadata(
        TCommand command,
        IReadOnlyList<AppendMetadata>? metadata)
    {
        var commandType = command?.GetType();
        if (commandType == null)
        {
            return (null, null);
        }

        // Get command metadata from registry
        var typeMetadata = _registry.GetMetadataByType(commandType);

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
}

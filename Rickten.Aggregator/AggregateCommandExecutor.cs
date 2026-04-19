using Rickten.EventStore;

namespace Rickten.Aggregator;

/// <summary>
/// Orchestrates the full command execution workflow: load state, validate expected version (if required), 
/// execute command, validate fold (pre-append safety check), append events, and optionally save snapshots.
/// This class provides the complete command execution logic with data safety guarantees.
/// State management (loading, fold validation, event persistence, snapshotting) is delegated to AggregateRepository.
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
    /// Workflow: load state, validate expected version (if required), execute command via decider, 
    /// validate fold (pre-append safety check), append events, and optionally save snapshots.
    /// </summary>
    /// <param name="streamIdentifier">The stream identifier.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="metadata">Metadata to attach to events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new state, stream pointer, and appended events.</returns>
    public async Task<(TState State, StreamPointer Pointer, IReadOnlyList<StreamEvent> Events)> ExecuteAsync(
        StreamIdentifier streamIdentifier,
        TCommand command,
        IReadOnlyList<AppendMetadata> metadata,
        CancellationToken cancellationToken = default)
    {
        var (state, currentPointer) = await _repository.LoadStateAsync(streamIdentifier, cancellationToken);

        var (expectedVersion, expectedVersionKey) = GetExpectedVersionFromMetadata(command, metadata);
        if (expectedVersion.HasValue && currentPointer != expectedVersion.Value)
        {
            var expectedPointer = streamIdentifier.At(expectedVersion.Value);
            throw new StreamVersionConflictException(
                expectedPointer,
                currentPointer,
                $"Version mismatch: expected version {expectedVersion.Value}, but current version is {currentPointer.Version}. " +
                $"The aggregate has changed since the decision was made.");
        }

        var events              = _decider.Execute(state, command);
        if (events.Count == 0)
        {
            return (state, currentPointer, []);
        }

        var newState            = _repository.ValidateFold(state, events);
        var filteredMetadata    = metadata.Filter(expectedVersionKey);
        var appendEvents        = events.ToAppendEvent(filteredMetadata);

        var appendedEvents      = await _repository.AppendEventsAsync(currentPointer, appendEvents, cancellationToken);
        var finalPointer        = appendedEvents.LastVersion();

        await _repository.SaveSnapshotIfNeededAsync(newState, currentPointer.Version, finalPointer, cancellationToken);
        return (newState, finalPointer, appendedEvents);
    }

    /// <summary>
    /// Extracts expected version from metadata if the command has an ExpectedVersionKey.
    /// Returns both the expected version and the key name so it can be filtered from persisted metadata.
    /// </summary>
    private (long? expectedVersion, string? expectedVersionKey) GetExpectedVersionFromMetadata(
        TCommand command,
        IReadOnlyList<AppendMetadata> metadata)
    {
        var commandType = command?.GetType();
        if (commandType == null)
        {
            return (null, null);
        }

        var typeMetadata = _registry.GetMetadataByType(commandType);
        var commandAttr = (typeMetadata?.AttributeInstance as CommandAttribute)
            ?? throw new InvalidOperationException(
                $"CRITICAL CONFIGURATION ERROR: Command type '{commandType.Name}' is not registered in the type metadata registry. " +
                $"This is a fatal setup error that must be fixed immediately. " +
                $"All command types must have a [Command] attribute and be registered via AddEventStore during service configuration. " +
                $"Ensure the assembly containing '{commandType.Name}' is included in the AddEventStore call. " +
                $"This error prevents command execution to avoid bypassing expected version validation.");

        var expectedVersionKey = commandAttr.ExpectedVersionKey;

        if (string.IsNullOrWhiteSpace(expectedVersionKey))
        {
            return (null, null);
        }

        var metadataItem = metadata.FirstOrDefault(m => string.Equals(m.Key, expectedVersionKey, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Command '{commandType.Name}' requires expected version metadata key '{expectedVersionKey}', but it was not provided.");

        var metadataValue = metadataItem.Value
            ?? throw new InvalidOperationException($"Command '{commandType.Name}' requires expected version metadata key '{expectedVersionKey}', but the value was null.");

        var version = ParseVersionFromMetadata(metadataValue, commandType.Name, expectedVersionKey);
        return (version, expectedVersionKey);
    }

    /// <summary>
    /// Parses a version number from metadata value, supporting common numeric types and parseable strings.
    /// </summary>
    private static long ParseVersionFromMetadata(object metadataValue, string commandTypeName, string metadataKey)
    {
        return metadataValue switch
        {
            long longValue => longValue,
            int intValue => intValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            string stringValue when long.TryParse(stringValue, out var parsedValue) => parsedValue,
            _ => throw new InvalidOperationException(
                $"Command '{commandTypeName}' expected version metadata key '{metadataKey}' has value of type '{metadataValue.GetType().Name}' which cannot be converted to long.")
        };
    }
}

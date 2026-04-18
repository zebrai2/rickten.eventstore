using Rickten.EventStore;

namespace Rickten.Aggregator;

/// <summary>
/// Provides utilities for loading and folding event streams into aggregate state.
/// </summary>
public interface IStateRunner
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
    Task<(TState State, long Version)> LoadStateAsync<TState>(
        IEventStore eventStore,
        IStateFolder<TState> folder,
        StreamIdentifier streamIdentifier,
        ISnapshotStore? snapshotStore = null,
        CancellationToken cancellationToken = default);

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
    Task<(TState State, long Version, IReadOnlyList<StreamEvent> Events)> ExecuteAsync<TState, TCommand>(
        IEventStore eventStore,
        IStateFolder<TState> folder,
        ICommandDecider<TState, TCommand> decider,
        StreamIdentifier streamIdentifier,
        TCommand command,
        EventStore.TypeMetadata.ITypeMetadataRegistry registry,
        ISnapshotStore? snapshotStore = null,
        IReadOnlyList<AppendMetadata>? metadata = null,
        CancellationToken cancellationToken = default);
}

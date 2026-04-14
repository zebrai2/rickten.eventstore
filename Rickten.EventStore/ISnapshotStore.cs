namespace Rickten.EventStore;

/// <summary>
/// Represents a store for persisting and retrieving stream snapshots.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>
    /// Loads the most recent snapshot for a stream.
    /// </summary>
    /// <param name="streamIdentifier">The identifier of the stream to load the snapshot for.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The snapshot if one exists, otherwise null.</returns>
    Task<Snapshot?> LoadSnapshotAsync(
        StreamIdentifier streamIdentifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a snapshot of a stream's current state.
    /// </summary>
    /// <param name="streamPointer">The stream pointer indicating the version being snapshotted.</param>
    /// <param name="state">The state to snapshot.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    Task SaveSnapshotAsync(
        StreamPointer streamPointer,
        object state,
        CancellationToken cancellationToken = default);
}

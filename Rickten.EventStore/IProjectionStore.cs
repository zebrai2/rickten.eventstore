namespace Rickten.EventStore;

/// <summary>
/// Represents a store for persisting and retrieving projections.
/// </summary>
public interface IProjectionStore
{
    /// <summary>
    /// Loads a projection by its key.
    /// </summary>
    /// <typeparam name="TState">The type of the projection state.</typeparam>
    /// <param name="projectionKey">The unique key identifying the projection.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The projection if it exists, otherwise null.</returns>
    Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a projection with its current state and global position.
    /// </summary>
    /// <typeparam name="TState">The type of the projection state.</typeparam>
    /// <param name="projectionKey">The unique key identifying the projection.</param>
    /// <param name="globalPosition">The global position of the last processed event.</param>
    /// <param name="state">The current state of the projection.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    Task SaveProjectionAsync<TState>(
        string projectionKey,
        long globalPosition,
        TState state,
        CancellationToken cancellationToken = default);
}

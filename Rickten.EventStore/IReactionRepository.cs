namespace Rickten.EventStore;

/// <summary>
/// Repository for managing reaction execution checkpoints.
/// </summary>
public interface IReactionRepository
{
    /// <summary>
    /// Loads a reaction checkpoint by its name.
    /// </summary>
    /// <param name="reactionName">The unique name of the reaction.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The reaction checkpoint if it exists, otherwise null.</returns>
    Task<ReactionCheckpoint?> LoadCheckpointAsync(
        string reactionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a reaction checkpoint.
    /// </summary>
    /// <param name="checkpoint">The reaction checkpoint to save.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    Task SaveCheckpointAsync(
        ReactionCheckpoint checkpoint,
        CancellationToken cancellationToken = default);
}

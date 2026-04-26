namespace Rickten.Reactor;

/// <summary>
/// Represents a trigger instance - a running, configured source that produces reaction contexts.
/// Multiple reactions can listen to the same trigger instance.
/// </summary>
public interface IReactionTrigger
{
    /// <summary>
    /// Gets the unique name of this trigger instance (e.g., "OnOrderSubmitted").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the trigger type that created this instance (e.g., "EventStore", "Recurring").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Listens for trigger occurrences and yields reaction contexts.
    /// Each context represents one trigger firing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop listening.</param>
    /// <returns>Async enumerable of reaction contexts.</returns>
    IAsyncEnumerable<ReactionContext> ListenAsync(
        CancellationToken cancellationToken);
}

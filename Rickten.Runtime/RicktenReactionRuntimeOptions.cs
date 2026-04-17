namespace Rickten.Runtime;

/// <summary>
/// Configuration options for a reaction runtime.
/// </summary>
public sealed class RicktenReactionRuntimeOptions
{
    /// <summary>
    /// Gets or sets whether the reaction runtime is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval between polling passes.
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the delay after an error before retrying (when ErrorBehavior is Retry).
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the error behavior when an exception occurs.
    /// Default is Stop.
    /// </summary>
    public RicktenRuntimeErrorBehavior ErrorBehavior { get; set; }
        = RicktenRuntimeErrorBehavior.Stop;

    /// <summary>
    /// Gets or sets an optional custom name for the reaction.
    /// If null, the reaction's name from the [Reaction] attribute will be used.
    /// </summary>
    public string? ReactionName { get; set; }
}

/// <summary>
/// Error behavior for reaction runtime.
/// </summary>
public enum RicktenRuntimeErrorBehavior
{
    /// <summary>
    /// Stop the hosted service and rethrow the exception.
    /// </summary>
    Stop,

    /// <summary>
    /// Log the error, delay, and retry.
    /// </summary>
    Retry
}

namespace Rickten.Runtime;

/// <summary>
/// Configuration options for the Rickten runtime.
/// </summary>
public class RicktenRuntimeOptions
{
    /// <summary>
    /// Gets or sets the default polling interval for hosted reactions.
    /// This is the delay between catch-up iterations when no specific interval is configured for a reaction.
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan DefaultPollingInterval { get; set; } = TimeSpan.FromSeconds(5);
}

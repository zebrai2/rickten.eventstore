namespace Rickten.Runtime;

/// <summary>
/// Abstraction for waiting/delaying execution.
/// Allows tests to control time without real delays.
/// </summary>
public interface IWaiter
{
    /// <summary>
    /// Waits for the specified duration.
    /// In production, uses Task.Delay. In tests, allows manual time advancement.
    /// </summary>
    /// <param name="duration">How long to wait.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task that completes after the duration or when cancelled.</returns>
    Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken = default);
}

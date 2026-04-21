namespace Rickten.TestUtils;

/// <summary>
/// A test waiter that signals when an iteration completes, allowing tests to synchronize.
/// </summary>
public sealed class SignalingWaiter : Runtime.IWaiter
{
    private readonly TaskCompletionSource<bool> _signal = new();

    /// <summary>
    /// Completes instantly but signals that an iteration has completed.
    /// </summary>
    public async Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Signal that an iteration completed
        _signal.TrySetResult(true);

        // Yield to allow other async operations to complete
        await Task.Yield();
    }

    /// <summary>
    /// Waits for the first iteration to complete.
    /// </summary>
    public Task WaitForIterationAsync(CancellationToken cancellationToken = default)
    {
        return _signal.Task.WaitAsync(cancellationToken);
    }
}

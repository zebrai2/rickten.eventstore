namespace Rickten.TestUtils;

/// <summary>
/// A test waiter that signals when an iteration completes and captures the polling interval.
/// </summary>
public sealed class SignalingWaiter : Runtime.IWaiter
{
    private readonly TaskCompletionSource<bool> _signal = new();
    private TimeSpan? _capturedDuration;

    /// <summary>
    /// Gets the duration that was passed to WaitAsync, or null if not yet called.
    /// </summary>
    public TimeSpan? CapturedDuration => _capturedDuration;

    /// <summary>
    /// Completes instantly but signals that an iteration has completed and captures the duration.
    /// </summary>
    public async Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Capture the duration on first call
        _capturedDuration ??= duration;

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

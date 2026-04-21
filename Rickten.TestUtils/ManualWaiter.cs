using Rickten.Runtime;

namespace Rickten.TestUtils;

/// <summary>
/// Test implementation of IWaiter that allows manual control over time advancement.
/// Waits complete immediately when AdvanceTime is called, with no real delays.
/// </summary>
public sealed class ManualWaiter : IWaiter
{
    private readonly Lock _lock = new();
    private readonly List<PendingWait> _pendingWaits = [];

    /// <summary>
    /// Creates a wait that will complete when AdvanceTime is called.
    /// Does not actually delay - completes instantly when time is advanced.
    /// </summary>
    public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _pendingWaits.Add(new PendingWait(tcs, cancellationToken));
        }

        // Register cancellation
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                lock (_lock)
                {
                    tcs.TrySetCanceled(cancellationToken);
                }
            });
        }

        return tcs.Task;
    }

    /// <summary>
    /// Advances time and completes all pending waits.
    /// This allows tests to control when delays complete without real waiting.
    /// </summary>
    public void AdvanceTime()
    {
        List<PendingWait> waitsToComplete;

        lock (_lock)
        {
            waitsToComplete = [.. _pendingWaits];
            _pendingWaits.Clear();
        }

        foreach (var wait in waitsToComplete)
        {
            if (!wait.CancellationToken.IsCancellationRequested)
            {
                wait.TaskCompletionSource.TrySetResult();
            }
        }
    }

    private record PendingWait(TaskCompletionSource TaskCompletionSource, CancellationToken CancellationToken);
}

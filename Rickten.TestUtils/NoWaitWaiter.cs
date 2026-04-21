using Rickten.Runtime;

namespace Rickten.TestUtils;

/// <summary>
/// Test implementation of IWaiter that never waits - all waits complete instantly.
/// Use this when you want the hosted service to run iterations as fast as possible
/// without any time control or delays.
/// </summary>
public sealed class NoWaitWaiter : IWaiter
{
    /// <summary>
    /// Completes instantly without any delay, but yields to let other async operations run.
    /// </summary>
    public async Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Yield to allow other async operations to complete and to prevent tight spinning
        await Task.Yield();
    }
}

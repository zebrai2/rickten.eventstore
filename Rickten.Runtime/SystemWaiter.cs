namespace Rickten.Runtime;

/// <summary>
/// Production implementation of IWaiter that uses real time delays.
/// </summary>
public sealed class SystemWaiter : IWaiter
{
    /// <inheritdoc />
    public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        return Task.Delay(duration, cancellationToken);
    }
}

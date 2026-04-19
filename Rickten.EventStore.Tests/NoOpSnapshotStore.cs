using System.Threading;
using System.Threading.Tasks;

namespace Rickten.EventStore;

/// <summary>
/// A no-op implementation of ISnapshotStore for testing purposes.
/// Use when snapshots are not needed but ISnapshotStore is required.
/// </summary>
public sealed class NoOpSnapshotStore : ISnapshotStore
{
    public static readonly NoOpSnapshotStore Instance = new();

    private NoOpSnapshotStore() { }

    public Task<Snapshot?> LoadSnapshotAsync(StreamIdentifier streamIdentifier, CancellationToken cancellationToken = default)
        => Task.FromResult<Snapshot?>(null);

    public Task SaveSnapshotAsync(StreamPointer streamPointer, object state, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

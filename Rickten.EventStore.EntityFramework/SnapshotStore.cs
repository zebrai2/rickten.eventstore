using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework.Entities;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Entity Framework Core implementation of <see cref="ISnapshotStore"/>.
/// States must be decorated with [Aggregate] attribute for type resolution.
/// </summary>
public sealed class SnapshotStore : ISnapshotStore
{
    private readonly EventStoreDbContext _context;
    private readonly WireTypeSerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotStore"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="registry">The type metadata registry.</param>
    public SnapshotStore(EventStoreDbContext context, ITypeMetadataRegistry registry)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serializer = new WireTypeSerializer(registry);
    }

    private bool IsInMemoryProvider()
    {
        return _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
    }

    /// <inheritdoc />
    public async Task<Snapshot?> LoadSnapshotAsync(
        StreamIdentifier streamIdentifier,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Snapshots
            .FirstOrDefaultAsync(
                s => s.StreamType == streamIdentifier.StreamType
                  && s.StreamIdentifier == streamIdentifier.Identifier,
                cancellationToken);

        if (entity == null)
        {
            return null;
        }

        var streamPointer = new StreamPointer(streamIdentifier, entity.Version);
        var state = _serializer.Deserialize(entity.State, entity.StateType);

        return new Snapshot(streamPointer, state);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses monotonic save semantics enforced at the database level: only updates the snapshot
    /// if the new StreamPointer.Version is greater than or equal to the existing snapshot version.
    /// Uses a conditional UPDATE with a WHERE clause that checks Version at the database boundary.
    /// This prevents stale snapshots from overwriting newer aggregate states, even under
    /// concurrent write conditions.
    /// </remarks>
    public async Task SaveSnapshotAsync(
        StreamPointer streamPointer,
        object state,
        CancellationToken cancellationToken = default)
    {
        var serializedState = _serializer.Serialize(state);
        var stateType = _serializer.GetWireName(state);

        while (true)
        {
            // Ensure clean change tracker for fresh database read
            _context.ChangeTracker.Clear();

            var now = DateTime.UtcNow;

            // Check if snapshot exists
            var currentVersion = await _context.Snapshots
                .Where(s => s.StreamType == streamPointer.Stream.StreamType
                         && s.StreamIdentifier == streamPointer.Stream.Identifier)
                .Select(s => (long?)s.Version)
                .FirstOrDefaultAsync(cancellationToken);

            if (currentVersion == null)
            {
                // Insert new snapshot
                var entity = new SnapshotEntity
                {
                    StreamType = streamPointer.Stream.StreamType,
                    StreamIdentifier = streamPointer.Stream.Identifier,
                    Version = streamPointer.Version,
                    StateType = stateType,
                    State = serializedState,
                    CreatedAt = now
                };
                _context.Snapshots.Add(entity);

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    return; // Success
                }
                catch (DbUpdateException)
                {
                    // Another process inserted first, retry the update path
                    continue;
                }
            }

            // Check monotonic condition: only update if new version >= existing
            if (streamPointer.Version < currentVersion.Value)
            {
                // Stale save, silently ignore
                return;
            }

            // Use ExecuteUpdate for real databases (atomic), fallback to tracked updates for InMemory
            if (IsInMemoryProvider())
            {
                // InMemory fallback: load, update, save (no race protection but acceptable for tests)
                var entity = await _context.Snapshots
                    .FirstAsync(s => s.StreamType == streamPointer.Stream.StreamType
                                  && s.StreamIdentifier == streamPointer.Stream.Identifier,
                        cancellationToken);

                // Re-check monotonic condition in case another test modified it
                if (streamPointer.Version >= entity.Version)
                {
                    entity.Version = streamPointer.Version;
                    entity.StateType = stateType;
                    entity.State = serializedState;
                    entity.CreatedAt = now;
                    await _context.SaveChangesAsync(cancellationToken);
                }
                return;
            }

            // Production path: atomic conditional update at database level
            var rowsAffected = await _context.Snapshots
                .Where(s => s.StreamType == streamPointer.Stream.StreamType
                         && s.StreamIdentifier == streamPointer.Stream.Identifier
                         && s.Version == currentVersion.Value)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Version, streamPointer.Version)
                    .SetProperty(s => s.StateType, stateType)
                    .SetProperty(s => s.State, serializedState)
                    .SetProperty(s => s.CreatedAt, now),
                    cancellationToken);

            if (rowsAffected > 0)
            {
                // Update succeeded
                return;
            }

            // Another process modified the snapshot between our read and update, retry
        }
    }
}

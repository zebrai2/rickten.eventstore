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
            .AsNoTracking()
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
        var now = DateTime.UtcNow;

        // Use ExecuteUpdate for real databases (atomic), fallback to tracked updates for InMemory
        if (IsInMemoryProvider())
        {
            // InMemory fallback: load or create, update if monotonic, save
            var entity = await _context.Snapshots
                .FirstOrDefaultAsync(s => s.StreamType == streamPointer.Stream.StreamType
                                       && s.StreamIdentifier == streamPointer.Stream.Identifier,
                    cancellationToken);

            if (entity == null)
            {
                // Insert new snapshot
                entity = new SnapshotEntity
                {
                    StreamType = streamPointer.Stream.StreamType,
                    StreamIdentifier = streamPointer.Stream.Identifier,
                    Version = streamPointer.Version,
                    StateType = stateType,
                    State = serializedState,
                    CreatedAt = now
                };
                _context.Snapshots.Add(entity);
            }
            else if (streamPointer.Version >= entity.Version)
            {
                // Update existing snapshot if not stale
                entity.Version = streamPointer.Version;
                entity.StateType = stateType;
                entity.State = serializedState;
                entity.CreatedAt = now;
            }
            else
            {
                // Stale save, ignore
                return;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        // Production path: try atomic conditional update first
        var rowsAffected = await _context.Snapshots
            .Where(s => s.StreamType == streamPointer.Stream.StreamType
                     && s.StreamIdentifier == streamPointer.Stream.Identifier
                     && s.Version <= streamPointer.Version)
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

        // No row was updated - either doesn't exist or is newer than this save
        var exists = await _context.Snapshots
            .AsNoTracking()
            .AnyAsync(s => s.StreamType == streamPointer.Stream.StreamType
                        && s.StreamIdentifier == streamPointer.Stream.Identifier,
                cancellationToken);

        if (exists)
        {
            // Snapshot exists but is newer than this save - stale, silently return
            return;
        }

        // Snapshot doesn't exist - try to insert
        var insertEntity = new SnapshotEntity
        {
            StreamType = streamPointer.Stream.StreamType,
            StreamIdentifier = streamPointer.Stream.Identifier,
            Version = streamPointer.Version,
            StateType = stateType,
            State = serializedState,
            CreatedAt = now
        };
        _context.Snapshots.Add(insertEntity);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return; // Insert succeeded
        }
        catch (DbUpdateException)
        {
            // Detach the failed insert to prevent tracking conflicts
            _context.Entry(insertEntity).State = EntityState.Detached;

            // Verify this was actually a duplicate-key race by checking if the row now exists
            exists = await _context.Snapshots
                .AsNoTracking()
                .AnyAsync(s => s.StreamType == streamPointer.Stream.StreamType
                            && s.StreamIdentifier == streamPointer.Stream.Identifier,
                    cancellationToken);

            if (!exists)
            {
                // Not a duplicate-key error, rethrow the original exception
                throw;
            }

            // Another process inserted first - perform one final conditional update
            rowsAffected = await _context.Snapshots
                .Where(s => s.StreamType == streamPointer.Stream.StreamType
                         && s.StreamIdentifier == streamPointer.Stream.Identifier
                         && s.Version <= streamPointer.Version)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Version, streamPointer.Version)
                    .SetProperty(s => s.StateType, stateType)
                    .SetProperty(s => s.State, serializedState)
                    .SetProperty(s => s.CreatedAt, now),
                    cancellationToken);

            // Whether it updated or not, we're done - if it didn't update, the existing row is newer
            return;
        }
    }
}

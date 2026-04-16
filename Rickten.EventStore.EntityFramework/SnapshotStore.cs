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
    public async Task SaveSnapshotAsync(
        StreamPointer streamPointer,
        object state,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Snapshots
            .FirstOrDefaultAsync(
                s => s.StreamType == streamPointer.Stream.StreamType
                  && s.StreamIdentifier == streamPointer.Stream.Identifier,
                cancellationToken);

        var serializedState = _serializer.Serialize(state);
        var stateType = _serializer.GetWireName(state);

        if (entity == null)
        {
            entity = new SnapshotEntity
            {
                StreamType = streamPointer.Stream.StreamType,
                StreamIdentifier = streamPointer.Stream.Identifier,
                Version = streamPointer.Version,
                StateType = stateType,
                State = serializedState,
                CreatedAt = DateTime.UtcNow
            };
            _context.Snapshots.Add(entity);
        }
        else
        {
            entity.Version = streamPointer.Version;
            entity.StateType = stateType;
            entity.State = serializedState;
            entity.CreatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

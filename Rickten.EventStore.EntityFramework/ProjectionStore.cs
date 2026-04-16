using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework.Entities;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Entity Framework Core implementation of <see cref="IProjectionStore"/>.
/// Projection state types must be decorated with [Projection] attribute for type resolution.
/// </summary>
public sealed class ProjectionStore : IProjectionStore
{
    private readonly EventStoreDbContext _context;
    private readonly WireTypeSerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectionStore"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="registry">The type metadata registry.</param>
    public ProjectionStore(EventStoreDbContext context, ITypeMetadataRegistry registry)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serializer = new WireTypeSerializer(registry);
    }

    /// <inheritdoc />
    public async Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Projections
            .FirstOrDefaultAsync(
                p => p.ProjectionKey == projectionKey,
                cancellationToken);

        if (entity == null)
        {
            return null;
        }

        // Deserialize using stored wire type
        var state = _serializer.Deserialize(entity.State, entity.StateType);

        // Validate that the deserialized state is assignable to TState
        if (state is not TState typedState)
        {
            var actualType = state.GetType();
            var requestedType = typeof(TState);

            throw new InvalidOperationException(
                $"Projection state type mismatch for key '{projectionKey}'. " +
                $"Stored wire type '{entity.StateType}' resolved to CLR type '{actualType.FullName}', " +
                $"which is not assignable to requested type '{requestedType.FullName}'. " +
                $"Ensure the projection state type matches the type used when saving.");
        }

        return new Projection<TState>(typedState, entity.GlobalPosition);
    }

    /// <inheritdoc />
    public async Task SaveProjectionAsync<TState>(
        string projectionKey,
        long globalPosition,
        TState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var entity = await _context.Projections
            .FirstOrDefaultAsync(
                p => p.ProjectionKey == projectionKey,
                cancellationToken);

        var serializedState = _serializer.Serialize(state);
        var stateType = _serializer.GetWireName(state);

        if (entity == null)
        {
            entity = new ProjectionEntity
            {
                ProjectionKey = projectionKey,
                GlobalPosition = globalPosition,
                StateType = stateType,
                State = serializedState,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Projections.Add(entity);
        }
        else
        {
            entity.GlobalPosition = globalPosition;
            entity.StateType = stateType;
            entity.State = serializedState;
            entity.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

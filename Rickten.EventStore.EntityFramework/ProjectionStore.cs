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

    private bool IsInMemoryProvider()
    {
        return _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
    }

    /// <inheritdoc />
    public async Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        CancellationToken cancellationToken = default)
    {
        return await LoadProjectionAsync<TState>(projectionKey, "system", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Projections
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Namespace == @namespace && p.ProjectionKey == projectionKey,
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
                $"Projection state type mismatch for key '{projectionKey}' in namespace '{@namespace}'. " +
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
        await SaveProjectionAsync(projectionKey, globalPosition, state, "system", cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses monotonic save semantics enforced at the database level: only updates the projection
    /// if the new globalPosition is greater than or equal to the existing GlobalPosition.
    /// Uses a conditional UPDATE with a WHERE clause that checks GlobalPosition at the database boundary.
    /// This prevents stale checkpoints from overwriting newer projection states, even under
    /// concurrent write conditions.
    /// </remarks>
    public async Task SaveProjectionAsync<TState>(
        string projectionKey,
        long globalPosition,
        TState state,
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var serializedState = _serializer.Serialize(state);
        var stateType = _serializer.GetWireName(state);

        while (true)
        {
            var now = DateTime.UtcNow;

            // Check if projection exists (use AsNoTracking to avoid polluting change tracker)
            var currentPosition = await _context.Projections
                .AsNoTracking()
                .Where(p => p.Namespace == @namespace && p.ProjectionKey == projectionKey)
                .Select(p => (long?)p.GlobalPosition)
                .FirstOrDefaultAsync(cancellationToken);

            if (currentPosition == null)
            {
                // Insert new projection
                var entity = new ProjectionEntity
                {
                    Namespace = @namespace,
                    ProjectionKey = projectionKey,
                    GlobalPosition = globalPosition,
                    StateType = stateType,
                    State = serializedState,
                    UpdatedAt = now
                };
                _context.Projections.Add(entity);

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    return; // Success
                }
                catch (DbUpdateException)
                {
                    // Another process inserted first, detach our failed insert and retry the update path
                    _context.Entry(entity).State = EntityState.Detached;
                    continue;
                }
            }

            // Check monotonic condition: only update if new position >= existing
            if (globalPosition < currentPosition.Value)
            {
                // Stale save, silently ignore
                return;
            }

            // Use ExecuteUpdate for real databases (atomic), fallback to tracked updates for InMemory
            if (IsInMemoryProvider())
            {
                // InMemory fallback: load, update, save (no race protection but acceptable for tests)
                var entity = await _context.Projections
                    .FirstAsync(p => p.Namespace == @namespace && p.ProjectionKey == projectionKey,
                        cancellationToken);

                // Re-check monotonic condition in case another test modified it
                if (globalPosition >= entity.GlobalPosition)
                {
                    entity.GlobalPosition = globalPosition;
                    entity.StateType = stateType;
                    entity.State = serializedState;
                    entity.UpdatedAt = now;
                    await _context.SaveChangesAsync(cancellationToken);
                }
                return;
            }

            // Production path: atomic conditional update at database level
            var rowsAffected = await _context.Projections
                .Where(p => p.Namespace == @namespace 
                         && p.ProjectionKey == projectionKey 
                         && p.GlobalPosition == currentPosition.Value)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.GlobalPosition, globalPosition)
                    .SetProperty(p => p.StateType, stateType)
                    .SetProperty(p => p.State, serializedState)
                    .SetProperty(p => p.UpdatedAt, now),
                    cancellationToken);

            if (rowsAffected > 0)
            {
                // Update succeeded
                return;
            }

            // Another process modified the projection between our read and update, retry
        }
    }
}

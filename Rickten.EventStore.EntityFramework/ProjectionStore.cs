using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework.Entities;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Entity Framework Core implementation of <see cref="IProjectionStore"/>.
/// Projection state types must be decorated with [Projection] attribute for type resolution.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ProjectionStore"/> class.
/// </remarks>
/// <param name="context">The database context.</param>
/// <param name="serializer">The wire type serializer.</param>
public sealed class ProjectionStore(EventStoreDbContext context, WireTypeSerializer serializer) : IProjectionStore
{
    private readonly EventStoreDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly WireTypeSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

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
        var now = DateTime.UtcNow;

        // Use ExecuteUpdate for real databases (atomic), fallback to tracked updates for InMemory
        if (IsInMemoryProvider())
        {
            // InMemory fallback: load or create, update if monotonic, save
            var entity = await _context.Projections
                .FirstOrDefaultAsync(p => p.Namespace == @namespace && p.ProjectionKey == projectionKey,
                    cancellationToken);

            if (entity == null)
            {
                // Insert new projection
                entity = new ProjectionEntity
                {
                    Namespace = @namespace,
                    ProjectionKey = projectionKey,
                    GlobalPosition = globalPosition,
                    StateType = stateType,
                    State = serializedState,
                    UpdatedAt = now
                };
                _context.Projections.Add(entity);
            }
            else if (globalPosition >= entity.GlobalPosition)
            {
                // Update existing projection if not stale
                entity.GlobalPosition = globalPosition;
                entity.StateType = stateType;
                entity.State = serializedState;
                entity.UpdatedAt = now;
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
        var rowsAffected = await _context.Projections
            .Where(p => p.Namespace == @namespace 
                     && p.ProjectionKey == projectionKey 
                     && p.GlobalPosition <= globalPosition)
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

        // No row was updated - either doesn't exist or is newer than this save
        var exists = await _context.Projections
            .AsNoTracking()
            .AnyAsync(p => p.Namespace == @namespace && p.ProjectionKey == projectionKey,
                cancellationToken);

        if (exists)
        {
            // Projection exists but is newer than this save - stale, silently return
            return;
        }

        // Projection doesn't exist - try to insert
        var insertEntity = new ProjectionEntity
        {
            Namespace = @namespace,
            ProjectionKey = projectionKey,
            GlobalPosition = globalPosition,
            StateType = stateType,
            State = serializedState,
            UpdatedAt = now
        };
        _context.Projections.Add(insertEntity);

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
            exists = await _context.Projections
                .AsNoTracking()
                .AnyAsync(p => p.Namespace == @namespace && p.ProjectionKey == projectionKey,
                    cancellationToken);

            if (!exists)
            {
                // Not a duplicate-key error, rethrow the original exception
                throw;
            }

            // Another process inserted first - perform one final conditional update
            rowsAffected = await _context.Projections
                .Where(p => p.Namespace == @namespace 
                         && p.ProjectionKey == projectionKey 
                         && p.GlobalPosition <= globalPosition)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.GlobalPosition, globalPosition)
                    .SetProperty(p => p.StateType, stateType)
                    .SetProperty(p => p.State, serializedState)
                    .SetProperty(p => p.UpdatedAt, now),
                    cancellationToken);

            // Whether it updated or not, we're done - if it didn't update, the existing row is newer
            return;
        }
    }
}

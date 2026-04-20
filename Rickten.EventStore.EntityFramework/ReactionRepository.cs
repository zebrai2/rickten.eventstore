using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework.Entities;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Entity Framework Core implementation of <see cref="IReactionRepository"/>.
/// </summary>
public sealed class ReactionRepository(EventStoreDbContext context) : IReactionRepository
{
    private readonly EventStoreDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ReactionCheckpoint?> LoadCheckpointAsync(
        string reactionName,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Reactions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReactionName == reactionName, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        return new ReactionCheckpoint(
            reactionName,
            entity.TriggerPosition,
            entity.ProjectionPosition);
    }

    public async Task SaveCheckpointAsync(
        ReactionCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Reactions
            .FirstOrDefaultAsync(r => r.ReactionName == checkpoint.ReactionName, cancellationToken);

        if (entity == null)
        {
            entity = new ReactionEntity
            {
                ReactionName = checkpoint.ReactionName,
                TriggerPosition = checkpoint.TriggerPosition,
                ProjectionPosition = checkpoint.ProjectionPosition,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Reactions.Add(entity);
        }
        else
        {
            entity.TriggerPosition = checkpoint.TriggerPosition;
            entity.ProjectionPosition = checkpoint.ProjectionPosition;
            entity.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

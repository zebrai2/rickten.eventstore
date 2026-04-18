using Microsoft.EntityFrameworkCore;

namespace Rickten.EventStore.EntityFramework;

public static class EventEntityExtensions
{
    /// <summary>
    /// Gets the current version of a stream. Returns 0 if the stream does not exist.
    /// </summary>
    public static async Task<long> GetCurrentVersionAsync(
        this IQueryable<Entities.EventEntity> events,
        StreamIdentifier stream,
        CancellationToken cancellationToken = default)
    {
        return await events
            .Where(e => e.StreamType == stream.StreamType
                     && e.StreamIdentifier == stream.Identifier)
            .MaxAsync(e => (long?)e.Version, cancellationToken) ?? 0;
    }
}

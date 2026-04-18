namespace Rickten.EventStore.EntityFramework;

public static class EventMetadataExtensions
{
    public static void AddSystemMetadata(this List<EventMetadata> metadata, long version, Guid batchId, Guid eventId)
    {
        metadata.Add(new EventMetadata(EventMetadataSource.System, EventMetadataKeys.EventId, eventId));
        metadata.Add(new EventMetadata(EventMetadataSource.System, EventMetadataKeys.BatchId, batchId));
        metadata.Add(new EventMetadata(EventMetadataSource.System, EventMetadataKeys.Timestamp, DateTime.UtcNow));
        metadata.Add(new EventMetadata(EventMetadataSource.System, EventMetadataKeys.StreamVersion, version));
    }

    /// <summary>
    /// Adds the batch correlation ID to the event metadata.
    /// Assumes CorrelationId has been filtered out during metadata transformation.
    /// </summary>
    /// <param name="metadata">The event's metadata list.</param>
    /// <param name="batchCorrelationId">The validated batch correlation ID to add.</param>
    public static void AddCorrelationId(this List<EventMetadata> metadata, EventMetadata batchCorrelationId)
    {
        metadata.Add(batchCorrelationId);
    }

    /// <summary>
    /// Gets metadata by key. Throws if multiple values with different sources exist for the same key.
    /// </summary>
    public static AppendMetadata? GetMetadata(this IReadOnlyList<AppendMetadata> metadata, string key)
    {
        var matches = metadata.Where(m => m.Key == key).ToList();

        if (matches.Count == 0)
            return null;

        if (matches.Count == 1)
            return matches[0];

        // Multiple entries for the same key - check if they have different sources
        var sources = matches
            .Select(m => m.Value is EventMetadata em ? em.Source : EventMetadataSource.Client)
            .Distinct()
            .ToList();

        if (sources.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple metadata entries found for key '{key}' with different sources: {string.Join(", ", sources)}. " +
                $"Each metadata key should have only one source.");
        }

        // Same source, just return the first one (though this is still odd)
        return matches[0];
    }
}
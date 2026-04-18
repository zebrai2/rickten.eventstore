namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Extension methods for AppendEvent collections.
/// </summary>
public static class AppendEventExtensions
{
    /// <summary>
    /// Extracts the event objects from a collection of AppendEvent instances.
    /// </summary>
    /// <param name="events">The AppendEvent collection.</param>
    /// <returns>An enumerable of event objects.</returns>
    public static IEnumerable<object> GetEvents(this IEnumerable<AppendEvent> events)
    {
        return events.Select(e => e.Event);
    }

    /// <summary>
    /// Gets the correlation ID metadata for the batch.
    /// Validates that all events have the same CorrelationId.
    /// If no CorrelationId is provided, generates one with System source.
    /// If CorrelationId is provided via AppendMetadata, it's tagged with Client source.
    /// </summary>
    /// <returns>EventMetadata containing the CorrelationId with appropriate source.</returns>
    /// <exception cref="InvalidOperationException">Thrown when events have different CorrelationIds.</exception>
    public static EventMetadata GetCorrelationIdStreamMetadata(this IReadOnlyList<AppendEvent> events)
    {
        object? batchCorrelationId = null;

        foreach (var appendEvent in events)
        {
            IReadOnlyList<AppendMetadata>? metadata = appendEvent.Metadata;
            if (metadata == null) continue;

            var correlationMetadata = metadata.GetMetadata(EventMetadataKeys.CorrelationId);
            if (correlationMetadata == null) continue;

            // Get the value directly - no source preservation needed
            object? currentValue = correlationMetadata.Value;

            if (batchCorrelationId == null)
            {
                // First CorrelationId found in batch
                batchCorrelationId = currentValue;
            }
            else if (!AreCorrelationIdsEqual(batchCorrelationId, currentValue))
            {
                // Different CorrelationId found - this is an error
                throw new InvalidOperationException(
                    $"All events in a batch must have the same CorrelationId. " +
                    $"Expected: {batchCorrelationId}, Found: {currentValue}");
            }
        }

        // If no CorrelationId provided, generate one for the entire batch with System source
        if (batchCorrelationId == null)
        {
            return new EventMetadata(EventMetadataSource.System, EventMetadataKeys.CorrelationId, Guid.NewGuid());
        }

        // AppendMetadata is client-provided, so use Client source
        return new EventMetadata(EventMetadataSource.Client, EventMetadataKeys.CorrelationId, batchCorrelationId);
    }

    private static bool AreCorrelationIdsEqual(object? first, object? second)
    {
        return (first, second) switch
        {
            (Guid firstGuid, Guid secondGuid) => firstGuid == secondGuid,
            (string firstStr, string secondStr) => firstStr == secondStr,
            (System.Text.Json.JsonElement firstJson, System.Text.Json.JsonElement secondJson) => 
                firstJson.ToString() == secondJson.ToString(),
            _ => Equals(first, second)
        };
    }
}

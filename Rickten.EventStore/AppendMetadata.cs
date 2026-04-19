using System.Runtime.CompilerServices;

namespace Rickten.EventStore;

/// <summary>
/// Represents metadata to be attached to an event during append operations.
/// The source will be automatically set to "Client" by the event store.
/// </summary>
/// <param name="Key">The metadata key (e.g., "CorrelationId", "UserId", "RequestId").</param>
/// <param name="Value">The metadata value.</param>
public sealed record AppendMetadata(
    string Key,
    object? Value)
{
    /// <summary>
    /// returns true if this metadata is a correlation ID, which is used for tracing related events across different streams and services.
    /// </summary>
    /// <returns></returns>
    public bool IsCorrelationId()
    {
        return Key == EventMetadataKeys.CorrelationId;
    }
}

public static class  AppendMetadataExtensions
{
    public static IReadOnlyList<AppendMetadata> Filter(this IReadOnlyList<AppendMetadata> metadata, string? filter)
        => string.IsNullOrWhiteSpace(filter) 
                ? metadata 
                : [.. metadata.Where(m => !string.Equals(m.Key, filter, StringComparison.Ordinal))];
}
namespace Rickten.EventStore;

/// <summary>
/// Represents metadata associated with an event with source tracking.
/// </summary>
/// <param name="Source">The source of the metadata (e.g., "Client", "System", "Application"). Used to differentiate between client-provided and system-generated metadata.</param>
/// <param name="Key">The metadata key (e.g., "CorrelationId", "UserId", "Timestamp").</param>
/// <param name="Value">The metadata value.</param>
public sealed record EventMetadata(
    string Source,
    string Key,
    object? Value);

namespace Rickten.EventStore;

/// <summary>
/// Represents metadata to be attached to an event during append operations.
/// The source will be automatically set to "Client" by the event store.
/// </summary>
/// <param name="Key">The metadata key (e.g., "CorrelationId", "UserId", "RequestId").</param>
/// <param name="Value">The metadata value.</param>
public sealed record AppendMetadata(
    string Key,
    object? Value);

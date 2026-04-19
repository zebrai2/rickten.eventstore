namespace Rickten.EventStore;

/// <summary>
/// Represents an event that has been persisted to a stream.
/// </summary>
/// <param name="StreamPointer">The stream pointer indicating the stream and version of this event.</param>
/// <param name="GlobalPosition">The global position of this event across all streams.</param>
/// <param name="Event">The event data.</param>
/// <param name="Metadata">The metadata associated with this event.</param>
public sealed record StreamEvent(
    StreamPointer StreamPointer,
    long GlobalPosition,
    object Event,
    IReadOnlyList<EventMetadata> Metadata);

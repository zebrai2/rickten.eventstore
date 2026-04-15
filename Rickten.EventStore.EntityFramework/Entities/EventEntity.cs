namespace Rickten.EventStore.EntityFramework.Entities;

/// <summary>
/// Entity representing a persisted event in the event store.
/// </summary>
public sealed class EventEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this event record.
    /// This serves as the global position across all streams.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the stream type (e.g., aggregate type).
    /// </summary>
    public required string StreamType { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier within the stream type.
    /// </summary>
    public required string StreamIdentifier { get; set; }

    /// <summary>
    /// Gets or sets the version number within the stream.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Gets the global position of this event across all streams (same as Id).
    /// </summary>
    public long GlobalPosition => Id;

    /// <summary>
    /// Gets or sets the event type name.
    /// </summary>
    public required string EventType { get; set; }

    /// <summary>
    /// Gets or sets the serialized event data as JSON.
    /// </summary>
    public required string EventData { get; set; }

    /// <summary>
    /// Gets or sets the serialized metadata as JSON.
    /// </summary>
    public required string Metadata { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this event was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

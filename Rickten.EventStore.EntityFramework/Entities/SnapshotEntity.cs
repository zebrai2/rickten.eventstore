namespace Rickten.EventStore.EntityFramework.Entities;

/// <summary>
/// Entity representing a stream snapshot in the event store.
/// </summary>
public sealed class SnapshotEntity
{
    /// <summary>
    /// Gets or sets the stream type (e.g., aggregate type).
    /// </summary>
    public required string StreamType { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier within the stream type.
    /// </summary>
    public required string StreamIdentifier { get; set; }

    /// <summary>
    /// Gets or sets the version number of the snapshot.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Gets or sets the type name of the state.
    /// </summary>
    public required string StateType { get; set; }

    /// <summary>
    /// Gets or sets the serialized state as JSON.
    /// </summary>
    public required string State { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this snapshot was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

namespace Rickten.EventStore.EntityFramework.Entities;

/// <summary>
/// Entity representing a projection in the event store.
/// </summary>
public sealed class ProjectionEntity
{
    /// <summary>
    /// Gets or sets the unique key identifying the projection.
    /// </summary>
    public required string ProjectionKey { get; set; }

    /// <summary>
    /// Gets or sets the global position of the last processed event.
    /// </summary>
    public long GlobalPosition { get; set; }

    /// <summary>
    /// Gets or sets the serialized projection state as JSON.
    /// </summary>
    public required string State { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this projection was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

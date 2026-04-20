namespace Rickten.EventStore.EntityFramework.Entities;

/// <summary>
/// Entity representing a reaction checkpoint in the event store.
/// </summary>
public sealed class ReactionEntity
{
    /// <summary>
    /// Gets or sets the unique reaction name.
    /// </summary>
    public required string ReactionName { get; set; }

    /// <summary>
    /// Gets or sets the global position of the last processed trigger event.
    /// </summary>
    public long TriggerPosition { get; set; }

    /// <summary>
    /// Gets or sets the global position of the last event applied to the projection view.
    /// </summary>
    public long ProjectionPosition { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this checkpoint was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

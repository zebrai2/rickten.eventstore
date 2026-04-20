namespace Rickten.EventStore;

/// <summary>
/// Represents a reaction's checkpoint tracking execution progress.
/// </summary>
/// <param name="ReactionName">The unique name of the reaction.</param>
/// <param name="TriggerPosition">The global position of the last processed trigger event.</param>
/// <param name="ProjectionPosition">The global position of the last event applied to the projection view.</param>
public sealed record ReactionCheckpoint(
    string ReactionName,
    long TriggerPosition,
    long ProjectionPosition);

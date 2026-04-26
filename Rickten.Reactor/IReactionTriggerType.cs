namespace Rickten.Reactor;

/// <summary>
/// Factory for creating trigger instances of a specific type.
/// Implement this interface to add new trigger mechanisms (EventStore, Recurring, Delayed, Endpoint, Manual).
/// </summary>
public interface IReactionTriggerType
{
    /// <summary>
    /// Gets the trigger type identifier (e.g., "EventStore", "Recurring", "Delayed").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Creates a trigger instance from a definition.
    /// </summary>
    /// <param name="definition">The trigger instance definition with name, type, and configuration.</param>
    /// <returns>A running trigger instance.</returns>
    IReactionTrigger Create(
        TriggerInstanceDefinition definition);
}

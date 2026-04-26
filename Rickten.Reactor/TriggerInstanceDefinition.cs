namespace Rickten.Reactor;

/// <summary>
/// Defines a trigger instance - a uniquely named, configured source that can start reactions.
/// Multiple reactions can share the same trigger instance.
/// </summary>
/// <param name="Name">Unique name for this trigger instance (e.g., "OnOrderSubmitted").</param>
/// <param name="Type">The trigger type that handles this instance (e.g., "EventStore", "Recurring").</param>
/// <param name="Configuration">Type-specific configuration for this trigger instance.</param>
public sealed record TriggerInstanceDefinition(
    string Name,
    string Type,
    IReadOnlyDictionary<string, object?> Configuration);

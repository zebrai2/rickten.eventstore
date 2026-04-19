using Rickten.EventStore.TypeMetadata;

namespace Rickten.Reactor;

/// <summary>
/// Optional attribute for reaction metadata and event filtering.
/// Used to optimize event queries by filtering at the store level.
/// </summary>
/// <param name="name">The reaction name (used for checkpointing and identification).</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReactionAttribute(string name) : Attribute, ITypeMetadata
{
    /// <summary>
    /// Gets the reaction name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or sets the event types this reaction triggers on.
    /// Used to filter events at the store level via LoadAllAsync.
    /// If null, all event types are processed.
    /// </summary>
    public string[]? EventTypes { get; init; }

    /// <summary>
    /// Gets or sets the polling interval in milliseconds for hosted reactions.
    /// When running in Rickten.Runtime, this controls how frequently the reaction catches up.
    /// Set to 0 (default) to use the runtime's default polling interval.
    /// Must be a positive value or 0.
    /// </summary>
    public int PollingIntervalMilliseconds { get; init; } = 0;

    /// <summary>
    /// Gets or sets a description of what this reaction does.
    /// </summary>
    public string? Description { get; init; }

    /// <inheritdoc />
    string? ITypeMetadata.GetWireName(Type decoratedType)
    {
        // Reactions use typed command execution, but provide a wire name for registry consistency
        // Include type name to ensure uniqueness when multiple reactions share a logical name
        return $"Reaction.{Name}.{decoratedType.Name}";
    }

    /// <inheritdoc />
    string? ITypeMetadata.GetAggregateName()
    {
        // Reactions don't belong to a single aggregate
        return null;
    }
}

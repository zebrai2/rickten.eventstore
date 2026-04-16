using Rickten.EventStore.TypeMetadata;

namespace Rickten.Projector;

/// <summary>
/// Optional attribute for projection metadata and event filtering.
/// Used to optimize event queries by filtering at the store level.
/// </summary>
/// <param name="name">The projection name (used for checkpointing and identification).</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ProjectionAttribute(string name) : Attribute, ITypeMetadata
{
    /// <summary>
    /// Gets the projection name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or sets the aggregate types this projection is interested in.
    /// Used to filter events at the store level via LoadAllAsync.
    /// If null, all aggregates are processed.
    /// </summary>
    public string[]? AggregateTypes { get; init; }

    /// <summary>
    /// Gets or sets the event types this projection is interested in.
    /// Used to filter events at the store level via LoadAllAsync.
    /// If null, all event types are processed.
    /// </summary>
    public string[]? EventTypes { get; init; }

    /// <summary>
    /// Gets or sets a description of what this projection does.
    /// </summary>
    public string? Description { get; init; }

    /// <inheritdoc />
    string? ITypeMetadata.GetWireName(Type decoratedType)
    {
        // Projections use typed serialization, but provide a wire name for registry consistency
        // Include type name to ensure uniqueness when multiple projections share a logical name
        return $"Projection.{Name}.{decoratedType.Name}";
    }

    /// <inheritdoc />
    string? ITypeMetadata.GetAggregateName()
    {
        // Projections don't belong to a single aggregate
        return null;
    }
}

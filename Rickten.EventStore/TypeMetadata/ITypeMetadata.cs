namespace Rickten.EventStore.TypeMetadata;

/// <summary>
/// Contract for attributes that participate in the type metadata registry.
/// Provides wire name generation and aggregate/category metadata for serialization.
/// </summary>
public interface ITypeMetadata
{
    /// <summary>
    /// Gets the wire name components used to generate the type's storage identifier.
    /// Format depends on the attribute type:
    /// - Events: "Aggregate.Name.vVersion"
    /// - Aggregates: "AggregateName.TypeName"
    /// - Commands: "Aggregate.TypeName"
    /// - Projections: Optional, may return null if not used for serialization
    /// </summary>
    string? GetWireName(Type decoratedType);

    /// <summary>
    /// Gets the aggregate or category name, if applicable.
    /// Used for grouping related types and filtering.
    /// </summary>
    string? GetAggregateName();
}

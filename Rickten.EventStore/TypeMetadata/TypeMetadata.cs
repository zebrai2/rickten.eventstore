namespace Rickten.EventStore.TypeMetadata;

/// <summary>
/// Represents metadata about a type decorated with one of the known attributes.
/// </summary>
public sealed record TypeMetadata
{
    /// <summary>
    /// Gets the CLR type.
    /// </summary>
    public required Type ClrType { get; init; }

    /// <summary>
    /// Gets the wire/storage name used for serialization.
    /// </summary>
    public required string WireName { get; init; }

    /// <summary>
    /// Gets the aggregate/category name, if applicable.
    /// </summary>
    public string? AggregateName { get; init; }

    /// <summary>
    /// Gets the attribute type that was found on this type.
    /// </summary>
    public required Type AttributeType { get; init; }

    /// <summary>
    /// Gets the attribute instance.
    /// </summary>
    public required Attribute AttributeInstance { get; init; }
}

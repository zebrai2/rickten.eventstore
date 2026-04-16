using System;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore;

/// <summary>
/// Decorates an event class with metadata for serialization and versioning.
/// </summary>
/// <param name="aggregate">The aggregate type this event belongs to.</param>
/// <param name="name">The name of the event.</param>
/// <param name="version">The version of the event schema.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EventAttribute(string aggregate, string name, int version) : Attribute, ITypeMetadata
{
    /// <summary>
    /// Gets the aggregate type this event belongs to.
    /// </summary>
    public string Aggregate { get; init; } = aggregate;
    /// <summary>
    /// Gets the name of the event.
    /// </summary>
    public string Name { get; init; } = name;
    /// <summary>
    /// Gets the version of the event schema.
    /// </summary>
    public int Version { get; init; } = version;

    /// <inheritdoc />
    string? ITypeMetadata.GetWireName(Type decoratedType)
    {
        return $"{Aggregate}.{Name}.v{Version}";
    }

    /// <inheritdoc />
    string? ITypeMetadata.GetAggregateName()
    {
        return Aggregate;
    }
}

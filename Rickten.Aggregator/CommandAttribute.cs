using Rickten.EventStore.TypeMetadata;

namespace Rickten.Aggregator;

/// <summary>
/// Marks a class as a command for a specific aggregate.
/// Used for validation, auto-discovery, and documentation generation.
/// </summary>
/// <param name="aggregate">The name of the aggregate this command belongs to.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class CommandAttribute(string aggregate) : Attribute, ITypeMetadata
{
    /// <summary>
    /// Gets the name of the aggregate this command belongs to.
    /// </summary>
    public string Aggregate { get; } = aggregate;

    /// <summary>
    /// Gets or sets a human-readable name for the command.
    /// If not specified, the type name is used.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets or sets a description of what this command does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the version mode for this command.
    /// LatestVersion (default): execute against the current aggregate stream version.
    /// ExpectedVersion: execute only if the stream is still at the caller's expected version.
    /// </summary>
    public CommandVersionMode VersionMode { get; init; } = CommandVersionMode.LatestVersion;

    /// <inheritdoc />
    string? ITypeMetadata.GetWireName(Type decoratedType)
    {
        return $"{Aggregate}.{decoratedType.Name}";
    }

    /// <inheritdoc />
    string? ITypeMetadata.GetAggregateName()
    {
        return Aggregate;
    }
}

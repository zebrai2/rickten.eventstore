namespace Rickten.Aggregator;

/// <summary>
/// Marks a command decider or state folder as belonging to a specific aggregate.
/// Event coverage validation is enabled by default (strict mode).
/// </summary>
/// <param name="name">The aggregate name (must match Event attribute aggregate names).</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class AggregateAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the aggregate name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or sets whether to validate that all events for this aggregate are handled.
    /// Default is true (strict mode). Set to false to disable validation.
    /// </summary>
    public bool ValidateEventCoverage { get; init; } = true;

    /// <summary>
    /// Gets or sets the snapshot interval for this aggregate.
    /// When > 0, StateRunner will automatically save snapshots every N events.
    /// Default is 0 (no automatic snapshots).
    /// </summary>
    public int SnapshotInterval { get; init; } = 0;
}

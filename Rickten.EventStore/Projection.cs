namespace Rickten.EventStore;

/// <summary>
/// Represents a projection's state at a specific global position.
/// </summary>
/// <typeparam name="TState">The type of the projection state.</typeparam>
/// <param name="State">The current state of the projection.</param>
/// <param name="GlobalPosition">The global position of the last processed event.</param>
public sealed record Projection<TState>(
    TState State,
    long GlobalPosition);

namespace Rickten.Aggregator;

/// <summary>
/// Folds events into state, transforming a stream of events into current aggregate state.
/// </summary>
/// <typeparam name="TState">The type of the aggregate state.</typeparam>
public interface IStateFolder<TState>
{
    int SnapshotInterval { get; }

    /// <summary>
    /// Gets the initial state before any events are applied.
    /// </summary>
    /// <returns>The initial state.</returns>
    TState InitialState();

    /// <summary>
    /// Applies a single event to the current state, producing a new state.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="event">The event to apply.</param>
    /// <returns>A new state with the event applied.</returns>
    TState Apply(TState state, object @event);
}

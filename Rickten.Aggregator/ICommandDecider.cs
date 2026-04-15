namespace Rickten.Aggregator;

/// <summary>
/// Decides what events should occur based on a command and current state.
/// </summary>
/// <typeparam name="TState">The aggregate state type.</typeparam>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandDecider<in TState, in TCommand>
{
    /// <summary>
    /// Executes business logic to decide what events should occur.
    /// </summary>
    /// <param name="state">The current aggregate state.</param>
    /// <param name="command">The command to execute.</param>
    /// <returns>A list of events to append. Returns empty list for idempotent operations.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the command violates business rules.</exception>
    IReadOnlyList<object> Execute(TState state, TCommand command);
}

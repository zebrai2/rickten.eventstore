using Rickten.EventStore;
using System.Reflection;

namespace Rickten.Aggregator;

/// <summary>
/// Abstract base class for command deciders that use pattern matching on command types.
/// Requires [Aggregate] attribute for proper event validation.
/// </summary>
/// <typeparam name="TState">The aggregate state type.</typeparam>
/// <typeparam name="TCommand">The base command type.</typeparam>
public abstract class CommandDecider<TState, TCommand> : ICommandDecider<TState, TCommand>
{
    private static readonly Lazy<string> _aggregateName = new(() =>
    {
        var implementationType = FindImplementationType();
        var attr = implementationType.GetCustomAttribute<AggregateAttribute>();

        if (attr == null)
        {
            throw new InvalidOperationException(
                $"CommandDecider implementation '{implementationType.Name}' must be decorated with [Aggregate] attribute. " +
                $"Add [Aggregate(\"YourAggregateName\")] to your class.");
        }

        return attr.Name;
    });

    /// <summary>
    /// Gets the aggregate name from the [Aggregate] attribute.
    /// </summary>
    protected string AggregateName => _aggregateName.Value;

    protected CommandDecider()
    {
        // Force validation on construction
        _ = _aggregateName.Value;
    }

    /// <inheritdoc />
    public IReadOnlyList<object> Execute(TState state, TCommand command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        // Validate command belongs to this aggregate
        ValidateCommandAggregate(command);

        // Validate before executing
        ValidateCommand(state, command);

        // Execute and get events
        var events = ExecuteCommand(state, command);

        // Validate that produced events belong to this aggregate
        ValidateEventAggregates(events);

        return events;
    }

    /// <summary>
    /// Validates a command against the current state. Override to add validation logic.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="command">The command to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    protected virtual void ValidateCommand(TState state, TCommand command)
    {
        // Default: no validation
    }

    /// <summary>
    /// Executes a command and returns the events that should be appended.
    /// Override this to handle your command types.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="command">The command to execute.</param>
    /// <returns>A list of events to append. Return empty list for idempotent operations.</returns>
    protected abstract IReadOnlyList<object> ExecuteCommand(TState state, TCommand command);

    /// <summary>
    /// Helper to return no events (idempotent operation).
    /// </summary>
    protected static IReadOnlyList<object> NoEvents() => [];

    /// <summary>
    /// Helper to return a single event.
    /// </summary>
    protected static IReadOnlyList<object> Event(object @event) => [@event];

    /// <summary>
    /// Helper to return multiple events.
    /// </summary>
    protected static IReadOnlyList<object> Events(params object[] events) => events;

    /// <summary>
    /// Helper to ensure a condition is true, throwing InvalidOperationException if false.
    /// </summary>
    /// <param name="condition">The condition to check.</param>
    /// <param name="errorMessage">The error message if condition is false.</param>
    /// <exception cref="InvalidOperationException">Thrown when condition is false.</exception>
    protected static void Require(bool condition, string errorMessage)
    {
        if (!condition)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    /// <summary>
    /// Helper to ensure a state property has an expected value.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="actual">The actual value.</param>
    /// <param name="expected">The expected value.</param>
    /// <param name="errorMessage">The error message if values don't match.</param>
    /// <exception cref="InvalidOperationException">Thrown when values don't match.</exception>
    protected static void RequireEqual<T>(T actual, T expected, string errorMessage)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    /// <summary>
    /// Helper to ensure a value is not null.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="errorMessage">The error message if value is null.</param>
    /// <exception cref="InvalidOperationException">Thrown when value is null.</exception>
    protected static void RequireNotNull<T>(T? value, string errorMessage) where T : class
    {
        if (value == null)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    /// <summary>
    /// Helper to create a StreamIdentifier for this aggregate.
    /// </summary>
    /// <param name="identifier">The unique identifier within the aggregate type.</param>
    /// <returns>A StreamIdentifier with the aggregate name and specified identifier.</returns>
    protected StreamIdentifier CreateStreamId(string identifier) => new(AggregateName, identifier);

    private static Type FindImplementationType()
    {
        // Walk stack to find concrete implementation
        var stackTrace = new System.Diagnostics.StackTrace();
        for (int i = 0; i < stackTrace.FrameCount; i++)
        {
            var method = stackTrace.GetFrame(i)?.GetMethod();
            var declaringType = method?.DeclaringType;

            if (declaringType != null && 
                !declaringType.IsAbstract && 
                typeof(CommandDecider<TState, TCommand>).IsAssignableFrom(declaringType))
            {
                return declaringType;
            }
        }

        // Fallback: scan loaded assemblies
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && typeof(CommandDecider<TState, TCommand>).IsAssignableFrom(t));

                var found = types.FirstOrDefault();
                if (found != null)
                    return found;
            }
            catch (ReflectionTypeLoadException)
            {
                // Skip
            }
        }

        throw new InvalidOperationException("Could not determine CommandDecider implementation type");
    }

    private void ValidateCommandAggregate(TCommand command)
    {
        var commandType = command!.GetType();
        var commandAttr = commandType.GetCustomAttribute<CommandAttribute>();

        if (commandAttr != null && commandAttr.Aggregate != AggregateName)
        {
            throw new InvalidOperationException(
                $"Command '{commandType.Name}' belongs to aggregate '{commandAttr.Aggregate}', " +
                $"but this CommandDecider is for aggregate '{AggregateName}'. " +
                $"Commands must match their aggregate's context.");
        }
    }

    private void ValidateEventAggregates(IReadOnlyList<object> events)
    {
        foreach (var @event in events)
        {
            var eventType = @event.GetType();
            var eventAttr = eventType.GetCustomAttribute<Rickten.EventStore.EventAttribute>();

            if (eventAttr != null && eventAttr.Aggregate != AggregateName)
            {
                throw new InvalidOperationException(
                    $"Event '{eventType.Name}' belongs to aggregate '{eventAttr.Aggregate}', " +
                    $"but this CommandDecider is for aggregate '{AggregateName}'. " +
                    $"Events must match their aggregate's context.");
            }
        }
    }
}

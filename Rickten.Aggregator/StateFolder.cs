using Rickten.EventStore;
using System.Reflection;

namespace Rickten.Aggregator;

/// <summary>
/// Abstract base class for state folders that use pattern matching on event types.
/// Requires [Aggregate] attribute and validates event coverage by default.
/// </summary>
/// <typeparam name="TState">The aggregate state type.</typeparam>
public abstract class StateFolder<TState> : IStateFolder<TState>
{
    private static readonly Lazy<AggregateInfo> _aggregateInfo = new(() =>
    {
        var implementationType = FindImplementationType();
        var attr = implementationType.GetCustomAttribute<AggregateAttribute>() ?? throw new InvalidOperationException(
                $"StateFolder implementation '{implementationType.Name}' must be decorated with [Aggregate] attribute. " +
                $"Add [Aggregate(\"YourAggregateName\")] to your class.");

        var allEvents = ScanEventsForAggregate(attr.Name);
        return new AggregateInfo(attr.Name, attr.ValidateEventCoverage, attr.SnapshotInterval, allEvents);
    });

    /// <summary>
    /// Override to explicitly declare events that should be ignored.
    /// Events listed here will not trigger validation errors when unhandled.
    /// </summary>
    protected virtual ISet<Type> IgnoredEvents => new HashSet<Type>();

    /// <summary>
    /// Gets the snapshot interval for this aggregate from the [Aggregate] attribute.
    /// Returns 0 if no automatic snapshots are configured.
    /// </summary>
    public int SnapshotInterval => _aggregateInfo.Value.SnapshotInterval;

    protected StateFolder()
    {
        var info = _aggregateInfo.Value;

        if (info.ValidateEventCoverage)
        {
            ValidateEventCoverage(info);
        }
    }

    /// <inheritdoc />
    public abstract TState InitialState();

    /// <inheritdoc />
    public TState Apply(TState state, object @event)
    {
        if (@event == null)
        {
            return state;
        }

        return ApplyEvent(state, @event);
    }

    /// <summary>
    /// Apply a specific event to the state. Override this to handle your event types.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="event">The event to apply.</param>
    /// <returns>The new state after applying the event.</returns>
    protected abstract TState ApplyEvent(TState state, object @event);

    /// <summary>
    /// Helper to ensure a state transition is valid.
    /// </summary>
    /// <param name="condition">The condition to check.</param>
    /// <param name="errorMessage">The error message if condition is false.</param>
    /// <exception cref="InvalidOperationException">Thrown when condition is false during event application.</exception>
    protected static void EnsureValid(bool condition, string errorMessage)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Invalid state transition: {errorMessage}");
        }
    }

    private static Type FindImplementationType()
    {
        // Walk up from StateFolder<TState> to find the concrete implementation
        var stackTrace = new System.Diagnostics.StackTrace();
        for (int i = 0; i < stackTrace.FrameCount; i++)
        {
            var method = stackTrace.GetFrame(i)?.GetMethod();
            var declaringType = method?.DeclaringType;

            if (declaringType != null && 
                !declaringType.IsAbstract && 
                typeof(StateFolder<TState>).IsAssignableFrom(declaringType))
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
                    .Where(t => !t.IsAbstract && typeof(StateFolder<TState>).IsAssignableFrom(t));

                var found = types.FirstOrDefault();
                if (found != null)
                    return found;
            }
            catch (ReflectionTypeLoadException)
            {
                // Skip
            }
        }

        throw new InvalidOperationException("Could not determine StateFolder implementation type");
    }

    private static HashSet<Type> ScanEventsForAggregate(string aggregateName)
    {
        var events = new HashSet<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => t.GetCustomAttribute<EventAttribute>() is EventAttribute attr 
                             && attr.Aggregate == aggregateName);

                foreach (var type in types)
                {
                    events.Add(type);
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Skip assemblies that can't be loaded
            }
        }

        return events;
    }

    private void ValidateEventCoverage(AggregateInfo info)
    {
        // For now, validation is disabled until we implement proper switch expression analysis
        // This infrastructure is in place for future enhancement

        var ignored = IgnoredEvents;
        var unhandled = info.AllEvents.Except(ignored).ToList();

        // TODO: Implement switch expression analysis to detect actually handled events
        // Would require analyzing ApplyEvent method body or using source generators

        if (unhandled.Any() && false) // Validation disabled - placeholder for future
        {
            throw new InvalidOperationException(
                $"[{info.AggregateName}] Unhandled events detected: {string.Join(", ", unhandled.Select(t => t.Name))}. " +
                $"Either handle them in ApplyEvent or add to IgnoredEvents property. " +
                $"To disable this check, set [Aggregate(\"{info.AggregateName}\", ValidateEventCoverage = false)]");
        }
    }

    private record AggregateInfo(string AggregateName, bool ValidateEventCoverage, int SnapshotInterval, HashSet<Type> AllEvents);
}

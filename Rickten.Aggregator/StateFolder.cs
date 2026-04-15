using Rickten.EventStore;
using System.Reflection;

namespace Rickten.Aggregator;

/// <summary>
/// Abstract base class for state folders using explicit event handler methods.
/// Requires [Aggregate] attribute. Define handlers as: protected TState When(EventType e, TState state).
/// </summary>
/// <typeparam name="TState">The aggregate state type.</typeparam>
public abstract class StateFolder<TState> : IStateFolder<TState>
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, AggregateInfo> _aggregateInfoCache = new();
    private readonly AggregateInfo _info;

    /// <summary>
    /// Override to explicitly declare events that should be ignored during validation.
    /// Events listed here will not trigger validation errors when unhandled.
    /// </summary>
    protected virtual ISet<Type> IgnoredEvents => new HashSet<Type>();

    /// <summary>
    /// Gets the snapshot interval for this aggregate from the [Aggregate] attribute.
    /// Returns 0 if no automatic snapshots are configured.
    /// </summary>
    public int SnapshotInterval => _info.SnapshotInterval;

    protected StateFolder()
    {
        var implementationType = GetType();
        _info = _aggregateInfoCache.GetOrAdd(implementationType, type =>
        {
            var attr = type.GetCustomAttribute<AggregateAttribute>() ?? throw new InvalidOperationException(
                    $"StateFolder implementation '{type.Name}' must be decorated with [Aggregate] attribute. " +
                    $"Add [Aggregate(\"YourAggregateName\")] to your class.");

            var allEvents = ScanEventsForAggregate(attr.Name);
            var handlers = ScanEventHandlers(type);
            return new AggregateInfo(attr.Name, attr.ValidateEventCoverage, attr.SnapshotInterval, allEvents, handlers);
        });

        if (_info.ValidateEventCoverage)
        {
            ValidateEventCoverage(_info);
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

        var eventType = @event.GetType();

        // Look up handler method for this event type
        if (_info.Handlers.TryGetValue(eventType, out var handler))
        {
            return (TState)handler.Invoke(this, [@event, state])!;
        }

        // No handler found - return state unchanged
        return state;
    }

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

    private static Dictionary<Type, MethodInfo> ScanEventHandlers(Type implementationType)
    {
        var handlers = new Dictionary<Type, MethodInfo>();

        // Find all protected methods named "When" with signature: TState When(EventType, TState)
        var methods = implementationType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name == "When" && m.ReturnType == typeof(TState));

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 2 && parameters[1].ParameterType == typeof(TState))
            {
                var eventType = parameters[0].ParameterType;
                handlers[eventType] = method;
            }
        }

        return handlers;
    }

    private void ValidateEventCoverage(AggregateInfo info)
    {
        var ignored = IgnoredEvents;
        var requiredEvents = info.AllEvents.Except(ignored).ToHashSet();
        var handledEvents = info.Handlers.Keys.ToHashSet();
        var unhandled = requiredEvents.Except(handledEvents).ToList();

        if (unhandled.Any())
        {
            throw new InvalidOperationException(
                $"[{info.AggregateName}] Unhandled events detected: {string.Join(", ", unhandled.Select(t => t.Name))}. " +
                $"Add handler methods: {string.Join(", ", unhandled.Select(t => $"protected {typeof(TState).Name} When({t.Name} e, {typeof(TState).Name} state)"))}. " +
                $"Or add to IgnoredEvents property. " +
                $"To disable this check, set [Aggregate(\"{info.AggregateName}\", ValidateEventCoverage = false)]");
        }
    }

    private record AggregateInfo(
        string AggregateName, 
        bool ValidateEventCoverage, 
        int SnapshotInterval, 
        HashSet<Type> AllEvents,
        Dictionary<Type, MethodInfo> Handlers);
}

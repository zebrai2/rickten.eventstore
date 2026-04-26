using Rickten.EventStore;

namespace Rickten.Reactor;

/// <summary>
/// Trigger type that listens to events in the event store.
/// Configuration: eventTypes (string[]), fromGlobalPosition (long?)
/// </summary>
public sealed class EventStoreTriggerType : IReactionTriggerType
{
    private readonly IEventStore _eventStore;

    public EventStoreTriggerType(IEventStore eventStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
    }

    public string Type => "EventStore";

    public IReactionTrigger Create(TriggerInstanceDefinition definition)
    {
        if (definition.Type != Type)
        {
            throw new ArgumentException($"Definition type '{definition.Type}' does not match this trigger type '{Type}'", nameof(definition));
        }

        // Parse configuration
        string[]? eventTypes = null;
        if (definition.Configuration.TryGetValue("eventTypes", out var eventTypesObj) && eventTypesObj != null)
        {
            eventTypes = eventTypesObj switch
            {
                string[] arr => arr,
                IEnumerable<string> enumerable => enumerable.ToArray(),
                _ => throw new InvalidOperationException($"eventTypes configuration must be a string array, got {eventTypesObj.GetType().Name}")
            };
        }

        long fromGlobalPosition = 0;
        if (definition.Configuration.TryGetValue("fromGlobalPosition", out var fromPosObj) && fromPosObj != null)
        {
            fromGlobalPosition = fromPosObj switch
            {
                long l => l,
                int i => i,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => throw new InvalidOperationException($"fromGlobalPosition must be a long, got {fromPosObj.GetType().Name}")
            };
        }

        return new EventStoreTrigger(
            name: definition.Name,
            eventStore: _eventStore,
            eventTypes: eventTypes,
            fromGlobalPosition: fromGlobalPosition);
    }
}

/// <summary>
/// Running event store trigger instance.
/// </summary>
internal sealed class EventStoreTrigger : IReactionTrigger
{
    private readonly IEventStore _eventStore;
    private readonly string[]? _eventTypes;
    private readonly long _fromGlobalPosition;

    public EventStoreTrigger(
        string name,
        IEventStore eventStore,
        string[]? eventTypes,
        long fromGlobalPosition)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _eventTypes = eventTypes;
        _fromGlobalPosition = fromGlobalPosition;
    }

    public string Name { get; }

    public string Type => "EventStore";

    public async IAsyncEnumerable<ReactionContext> ListenAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var streamEvent in _eventStore.LoadAllAsync(
            fromGlobalPosition: _fromGlobalPosition,
            eventsFilter: _eventTypes,
            cancellationToken: cancellationToken))
        {
            var context = new ReactionContext(new Dictionary<string, object?>
            {
                ["trigger.name"] = Name,
                ["trigger.type"] = Type,
                ["event.globalPosition"] = streamEvent.GlobalPosition
            });

            yield return context;
        }
    }
}

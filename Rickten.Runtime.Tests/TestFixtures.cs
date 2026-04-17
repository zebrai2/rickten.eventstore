using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.EventStore.TypeMetadata;
using Rickten.Projector;
using Rickten.Reactor;

namespace Rickten.Runtime.Tests;

// Test events
[Event("TestEntity", "Created", 1)]
public record TestEntityCreatedEvent(string EntityId, string Name);

[Event("TestEntity", "Updated", 1)]
public record TestEntityUpdatedEvent(string EntityId, string NewName);

// Test command
[Command("TestAggregate")]
public record TestProcessCommand(string AggregateId, string Reason);

// Test aggregate event
[Event("TestAggregate", "Processed", 1)]
public record TestAggregateProcessedEvent(string AggregateId, string Reason);

// Test aggregate state
[Aggregate("TestAggregate")]
public record TestAggregateState(string AggregateId, int ProcessCount);

// Test state folder
public class TestAggregateStateFolder : StateFolder<TestAggregateState>
{
    public TestAggregateStateFolder(ITypeMetadataRegistry registry) : base(registry) { }

    public override TestAggregateState InitialState() => new TestAggregateState("", 0);

    protected TestAggregateState When(TestAggregateProcessedEvent e, TestAggregateState state)
    {
        return state with
        {
            AggregateId = e.AggregateId,
            ProcessCount = state.ProcessCount + 1
        };
    }
}

// Test command decider
public class TestCommandDecider : CommandDecider<TestAggregateState, TestProcessCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(TestAggregateState state, TestProcessCommand command)
    {
        return Event(new TestAggregateProcessedEvent(command.AggregateId, command.Reason));
    }
}

// Test projection view
public record TestEntityView(Dictionary<string, string> Entities);

// Test projection
[Rickten.Projector.Projection("TestEntityIndex")]
public class TestEntityProjection : Rickten.Projector.Projection<TestEntityView>
{
    public override TestEntityView InitialView() =>
        new TestEntityView(new Dictionary<string, string>());

    protected override TestEntityView ApplyEvent(TestEntityView view, StreamEvent streamEvent)
    {
        return streamEvent.Event switch
        {
            TestEntityCreatedEvent evt => AddEntity(view, evt.EntityId, evt.Name),
            TestEntityUpdatedEvent evt => UpdateEntity(view, evt.EntityId, evt.NewName),
            _ => view
        };
    }

    private TestEntityView AddEntity(TestEntityView view, string entityId, string name)
    {
        var entities = new Dictionary<string, string>(view.Entities)
        {
            [entityId] = name
        };
        return view with { Entities = entities };
    }

    private TestEntityView UpdateEntity(TestEntityView view, string entityId, string newName)
    {
        if (!view.Entities.ContainsKey(entityId))
        {
            return view;
        }

        var entities = new Dictionary<string, string>(view.Entities)
        {
            [entityId] = newName
        };
        return view with { Entities = entities };
    }
}

// Test reaction
[Reaction("TestReaction", EventTypes = ["TestEntity.Updated"])]
public class TestReaction : Reaction<TestEntityView, TestProcessCommand>
{
    private readonly TestEntityProjection _projection;

    public TestReaction(ITypeMetadataRegistry registry)
        : base(registry)
    {
        _projection = new TestEntityProjection();
    }

    public override IProjection<TestEntityView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(TestEntityView view, StreamEvent trigger)
    {
        if (trigger.Event is TestEntityUpdatedEvent evt && view.Entities.ContainsKey(evt.EntityId))
        {
            // Process the entity with the same ID as the aggregate
            yield return new StreamIdentifier("TestAggregate", evt.EntityId);
        }
    }

    protected override TestProcessCommand BuildCommand(StreamIdentifier stream, TestEntityView view, StreamEvent trigger)
    {
        return new TestProcessCommand(stream.StreamId, "Entity updated");
    }
}

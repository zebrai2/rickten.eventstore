using Rickten.EventStore;
using Xunit;

namespace Rickten.Aggregator.Tests;

public class SnapshotTests
{
    [Fact]
    public void AggregateAttribute_DefaultSnapshotInterval_IsZero()
    {
        var attr = new AggregateAttribute("Test");
        Assert.Equal(0, attr.SnapshotInterval);
    }

    [Fact]
    public void AggregateAttribute_CanSetSnapshotInterval()
    {
        var attr = new AggregateAttribute("Test") { SnapshotInterval = 50 };
        Assert.Equal(50, attr.SnapshotInterval);
    }

    [Fact]
    public void StateFolder_ExposesSnapshotInterval()
    {
        var folder = new TestStateFolder();
        Assert.Equal(25, folder.SnapshotInterval);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutSnapshotStore_DoesNotSnapshot()
    {
        var eventStore = new InMemoryEventStore();
        var folder = new TestStateFolder();
        var decider = new TestCommandDecider();
        var streamId = new StreamIdentifier("Test", "1");

        var (state, version, events) = await StateRunner.ExecuteAsync(
            eventStore,
            folder,
            decider,
            streamId,
            new TestCommand.Increment(),
            snapshotStore: null);

        Assert.Equal(1, state.Count);
        Assert.Equal(1, version);
        Assert.Single(events);
    }

    [Fact]
    public async Task ExecuteAsync_WithSnapshotStore_ButNoInterval_DoesNotSnapshot()
    {
        var eventStore = new InMemoryEventStore();
        var snapshotStore = new InMemorySnapshotStore();
        var folder = new NoSnapshotStateFolder();
        var decider = new NoSnapshotCommandDecider();
        var streamId = new StreamIdentifier("NoSnapshot", "1");

        // Execute multiple commands
        for (int i = 0; i < 10; i++)
        {
            await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new NoSnapshotCommand.Increment(),
                snapshotStore);
        }

        // No snapshots should be saved
        var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task ExecuteAsync_WithSnapshotInterval_SavesSnapshotsAtInterval()
    {
        var eventStore = new InMemoryEventStore();
        var snapshotStore = new InMemorySnapshotStore();
        var folder = new TestStateFolder(); // SnapshotInterval = 25
        var decider = new TestCommandDecider();
        var streamId = new StreamIdentifier("Test", "1");

        // Execute 50 commands (should snapshot at version 25 and 50)
        for (int i = 0; i < 50; i++)
        {
            await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new TestCommand.Increment(),
                snapshotStore);
        }

        // Should have snapshot at version 50
        var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.NotNull(snapshot);
        Assert.Equal(50, snapshot.StreamPointer.Version);

        var state = (TestState)snapshot.State;
        Assert.Equal(50, state.Count);
    }

    [Fact]
    public async Task ExecuteAsync_SnapshotOnlyAtExactInterval()
    {
        var eventStore = new InMemoryEventStore();
        var snapshotStore = new InMemorySnapshotStore();
        var folder = new TestStateFolder(); // SnapshotInterval = 25
        var decider = new TestCommandDecider();
        var streamId = new StreamIdentifier("Test", "1");

        // Execute 26 commands (should only snapshot at version 25, not 26)
        for (int i = 0; i < 26; i++)
        {
            await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new TestCommand.Increment(),
                snapshotStore);
        }

        var snapshot = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.NotNull(snapshot);
        Assert.Equal(25, snapshot.StreamPointer.Version); // Last snapshot at 25, not 26
    }

    [Fact]
    public async Task ExecuteAsync_IdempotentCommand_DoesNotSnapshot()
    {
        var eventStore = new InMemoryEventStore();
        var snapshotStore = new InMemorySnapshotStore();
        var folder = new TestStateFolder();
        var decider = new TestCommandDecider();
        var streamId = new StreamIdentifier("Test", "1");

        // Set up state at version 25
        for (int i = 0; i < 25; i++)
        {
            await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new TestCommand.Increment(),
                snapshotStore);
        }

        var snapshotBefore = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.NotNull(snapshotBefore);

        // Execute idempotent command (returns no events)
        await StateRunner.ExecuteAsync(
            eventStore,
            folder,
            decider,
            streamId,
            new TestCommand.Noop(),
            snapshotStore);

        // Should still be at version 25 (no new snapshot)
        var snapshotAfter = await snapshotStore.LoadSnapshotAsync(streamId);
        Assert.Equal(snapshotBefore.StreamPointer.Version, snapshotAfter.StreamPointer.Version);
    }
}

// Test domain
public record TestState(int Count);

public abstract record TestCommand
{
    public record Increment : TestCommand;
    public record Noop : TestCommand;
}

[Event("Test", "Incremented", 1)]
public record TestIncremented;

[Aggregate("Test", SnapshotInterval = 25)]
public class TestStateFolder : StateFolder<TestState>
{
    public override TestState InitialState() => new(0);

    protected override TestState ApplyEvent(TestState state, object @event)
    {
        return @event switch
        {
            TestIncremented => state with { Count = state.Count + 1 },
            _ => state
        };
    }
}

[Aggregate("Test")]
public class TestCommandDecider : CommandDecider<TestState, TestCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(TestState state, TestCommand command)
    {
        return command switch
        {
            TestCommand.Increment => Event(new TestIncremented()),
            TestCommand.Noop => NoEvents(),
            _ => NoEvents()
        };
    }
}

// No snapshot configuration
public record NoSnapshotState(int Count);

public abstract record NoSnapshotCommand
{
    public record Increment : NoSnapshotCommand;
}

[Event("NoSnapshot", "Incremented", 1)]
public record NoSnapshotIncremented;

[Aggregate("NoSnapshot")] // No SnapshotInterval
public class NoSnapshotStateFolder : StateFolder<NoSnapshotState>
{
    public override NoSnapshotState InitialState() => new(0);

    protected override NoSnapshotState ApplyEvent(NoSnapshotState state, object @event)
    {
        return @event switch
        {
            NoSnapshotIncremented => state with { Count = state.Count + 1 },
            _ => state
        };
    }
}

[Aggregate("NoSnapshot")]
public class NoSnapshotCommandDecider : CommandDecider<NoSnapshotState, NoSnapshotCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(NoSnapshotState state, NoSnapshotCommand command)
    {
        return command switch
        {
            NoSnapshotCommand.Increment => Event(new NoSnapshotIncremented()),
            _ => NoEvents()
        };
    }
}

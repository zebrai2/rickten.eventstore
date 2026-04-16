using Rickten.EventStore;
using Rickten.EventStore.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
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
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
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
    }

    [Fact]
    public async Task ExecuteAsync_WithSnapshotStore_ButNoInterval_DoesNotSnapshot()
    {
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
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
    }

    [Fact]
    public async Task ExecuteAsync_WithSnapshotInterval_SavesSnapshotsAtInterval()
    {
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
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
    }

    [Fact]
    public async Task ExecuteAsync_SnapshotOnlyAtExactInterval()
    {
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
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
    }

    [Fact]
    public async Task ExecuteAsync_IdempotentCommand_DoesNotSnapshot()
    {
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
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

    [Fact]
    public async Task LoadStateAsync_WithSnapshot_StartsFromSnapshot()
    {
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
            var folder = new TestStateFolder();
            var decider = new TestCommandDecider();
            var streamId = new StreamIdentifier("Test", "1");

            // Create 50 events (will snapshot at 25 and 50)
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

            // Add 10 more events after the snapshot
            for (int i = 0; i < 10; i++)
            {
                await StateRunner.ExecuteAsync(
                    eventStore,
                    folder,
                    decider,
                    streamId,
                    new TestCommand.Increment(),
                    snapshotStore);
            }

            // Load state with snapshot - should start from version 50 snapshot
            var (state, version) = await StateRunner.LoadStateAsync(
                eventStore,
                folder,
                streamId,
                snapshotStore);

            Assert.Equal(60, state.Count);
            Assert.Equal(60, version);
        }
    }

    [Fact]
    public async Task LoadStateAsync_WithoutSnapshot_LoadsFromBeginning()
    {
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var folder = new TestStateFolder();
            var decider = new TestCommandDecider();
            var streamId = new StreamIdentifier("Test", "1");

            // Create 30 events
            for (int i = 0; i < 30; i++)
            {
                await StateRunner.ExecuteAsync(
                    eventStore,
                    folder,
                    decider,
                    streamId,
                    new TestCommand.Increment());
            }

            // Load state without snapshot store - should load all events
            var (state, version) = await StateRunner.LoadStateAsync(
                eventStore,
                folder,
                streamId);

            Assert.Equal(30, state.Count);
            Assert.Equal(30, version);
        }
    }
}

// Test domain
[Aggregate("Test", SnapshotInterval = 25)]
public record TestState(int Count);

public abstract record TestCommand
{
    public record Increment : TestCommand;
    public record Noop : TestCommand;
}

[Event("Test", "Incremented", 1)]
public record TestIncremented;

public class TestStateFolder : StateFolder<TestState>
{
    public override TestState InitialState() => new(0);

    protected TestState When(TestIncremented e, TestState state)
    {
        return state with { Count = state.Count + 1 };
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
[Aggregate("NoSnapshot")] // No SnapshotInterval - defaults to 0
public record NoSnapshotState(int Count);

public abstract record NoSnapshotCommand
{
    public record Increment : NoSnapshotCommand;
}

[Event("NoSnapshot", "Incremented", 1)]
public record NoSnapshotIncremented;

public class NoSnapshotStateFolder : StateFolder<NoSnapshotState>
{
    public override NoSnapshotState InitialState() => new(0);

    protected NoSnapshotState When(NoSnapshotIncremented e, NoSnapshotState state)
    {
        return state with { Count = state.Count + 1 };
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

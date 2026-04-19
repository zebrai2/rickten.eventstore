using Rickten.Aggregator;
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
        var registry = TestTypeMetadataRegistry.Create();
        var folder = new TestStateFolder(registry);
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
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new TestStateFolder(registry);
            var decider = new TestCommandDecider();
            var streamId = new StreamIdentifier("Test", "1");
            var AggregateRepository = new AggregateRepository<TestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<TestState, TestCommand>(AggregateRepository, decider, registry);

            var (state, pointer, events) = await executor.ExecuteAsync(
                streamId,
                new TestCommand.Increment(),
                metadata: []);

            Assert.Equal(1, state.Count);
            Assert.Equal(1, pointer.Version);
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
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new NoSnapshotStateFolder(registry);
            var decider = new NoSnapshotCommandDecider();
            var streamId = new StreamIdentifier("NoSnapshot", "1");
            var AggregateRepository = new AggregateRepository<NoSnapshotState>(eventStore, folder, snapshotStore);
            var executor = new AggregateCommandExecutor<NoSnapshotState, NoSnapshotCommand>(AggregateRepository, decider, registry);

            // Execute multiple commands
            for (int i = 0; i < 10; i++)
            {
                await executor.ExecuteAsync(
                    streamId,
                    new NoSnapshotCommand.Increment(),
                    metadata: []);
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
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new TestStateFolder(registry); // SnapshotInterval = 25
            var decider = new TestCommandDecider();
            var streamId = new StreamIdentifier("Test", "1");
            var AggregateRepository = new AggregateRepository<TestState>(eventStore, folder, snapshotStore);
            var executor = new AggregateCommandExecutor<TestState, TestCommand>(AggregateRepository, decider, registry);

            // Execute 50 commands (should snapshot at version 25 and 50)
            for (int i = 0; i < 50; i++)
            {
                await executor.ExecuteAsync(
                    streamId,
                    new TestCommand.Increment(),
                    metadata: []);
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
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new TestStateFolder(registry); // SnapshotInterval = 25
            var decider = new TestCommandDecider();
            var streamId = new StreamIdentifier("Test", "1");
            var AggregateRepository = new AggregateRepository<TestState>(eventStore, folder, snapshotStore);
            var executor = new AggregateCommandExecutor<TestState, TestCommand>(AggregateRepository, decider, registry);

            // Execute 26 commands (should only snapshot at version 25, not 26)
            for (int i = 0; i < 26; i++)
            {
                await executor.ExecuteAsync(
                    streamId,
                    new TestCommand.Increment(),
                    metadata: []);
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
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new TestStateFolder(registry);
            var decider = new TestCommandDecider();
            var streamId = new StreamIdentifier("Test", "1");
            var AggregateRepository = new AggregateRepository<TestState>(eventStore, folder, snapshotStore);
            var executor = new AggregateCommandExecutor<TestState, TestCommand>(AggregateRepository, decider, registry);

            // Set up state at version 25
            for (int i = 0; i < 25; i++)
            {
                await executor.ExecuteAsync(
                    streamId,
                    new TestCommand.Increment(),
                    metadata: []);
            }

            var snapshotBefore = await snapshotStore.LoadSnapshotAsync(streamId);
            Assert.NotNull(snapshotBefore);

            // Execute idempotent command (returns no events)
            await executor.ExecuteAsync(
                streamId,
                new TestCommand.Noop(),
                metadata: []);

            // Should still be at version 25 (no new snapshot)
            var snapshotAfter = await snapshotStore.LoadSnapshotAsync(streamId);
            Assert.NotNull(snapshotAfter);
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
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new TestStateFolder(registry);
            var decider = new TestCommandDecider();
            var streamId = new StreamIdentifier("Test", "1");
            var AggregateRepository = new AggregateRepository<TestState>(eventStore, folder, snapshotStore);
            var executor = new AggregateCommandExecutor<TestState, TestCommand>(AggregateRepository, decider, registry);

            // Create 50 events (will snapshot at 25 and 50)
            for (int i = 0; i < 50; i++)
            {
                await executor.ExecuteAsync(
                    streamId,
                    new TestCommand.Increment(),
                    metadata: []);
            }

            // Add 10 more events after the snapshot
            for (int i = 0; i < 10; i++)
            {
                await executor.ExecuteAsync(
                    streamId,
                    new TestCommand.Increment(),
                    metadata: []);
            }

            // Load state with snapshot - should start from version 50 snapshot
            var (state, pointer) = await AggregateRepository.LoadStateAsync(
                streamId);

            Assert.Equal(60, state.Count);
            Assert.Equal(60, pointer.Version);
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
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new TestStateFolder(registry);
            var decider = new TestCommandDecider();
            var streamId = new StreamIdentifier("Test", "1");
            var AggregateRepository = new AggregateRepository<TestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<TestState, TestCommand>(AggregateRepository, decider, registry);

            // Create 30 events
            for (int i = 0; i < 30; i++)
            {
                await executor.ExecuteAsync(
                    streamId,
                    new TestCommand.Increment(),
                    metadata: []);
            }

            // Load state without snapshot store - should load all events
            var (state, pointer) = await AggregateRepository.LoadStateAsync(
                streamId);

            Assert.Equal(30, state.Count);
            Assert.Equal(30, pointer.Version);
        }
    }
}

// Test domain
[Aggregate("Test", SnapshotInterval = 25)]
public record TestState(int Count);

[Command("Test")]
public abstract record TestCommand
{
    [Command("Test")]
    public record Increment : TestCommand;

    [Command("Test")]
    public record Noop : TestCommand;
}

[Event("Test", "Incremented", 1)]
public record TestIncremented;

public class TestStateFolder : StateFolder<TestState>
{
    public TestStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override TestState InitialState() => new(0);

    protected TestState When(TestIncremented e, TestState state)
    {
        return state with { Count = state.Count + 1 };
    }
}

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

[Command("NoSnapshot")]
public abstract record NoSnapshotCommand
{
    [Command("NoSnapshot")]
    public record Increment : NoSnapshotCommand;
}

[Event("NoSnapshot", "Incremented", 1)]
public record NoSnapshotIncremented;

public class NoSnapshotStateFolder : StateFolder<NoSnapshotState>
{
    public NoSnapshotStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override NoSnapshotState InitialState() => new(0);

    protected NoSnapshotState When(NoSnapshotIncremented e, NoSnapshotState state)
    {
        return state with { Count = state.Count + 1 };
    }
}

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

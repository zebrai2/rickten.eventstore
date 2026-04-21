using Rickten.Aggregator;
using Rickten.EventStore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Rickten.Aggregator.Tests;

/// <summary>
/// Tests for stateless command execution.
/// Verifies that stateless commands skip state loading and fold validation,
/// but still enforce expected version when configured via ExpectedVersionKey.
/// </summary>
public class StatelessCommandTests
{
    [Fact]
    public void CommandAttribute_DefaultStateless_IsFalse()
    {
        // Arrange & Act
        var attribute = new CommandAttribute("TestAggregate");

        // Assert
        Assert.False(attribute.Stateless);
    }

    [Fact]
    public void CommandAttribute_CanSetStateless()
    {
        // Arrange & Act
        var attribute = new CommandAttribute("TestAggregate")
        {
            Stateless = true
        };

        // Assert
        Assert.True(attribute.Stateless);
    }

    [Fact]
    public async Task ExecuteAsync_WithStatelessCommand_ExecutesSuccessfully()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new StatelessTestStateFolder(registry);
            var decider = new StatelessTestDecider();
            var streamId = new StreamIdentifier("StatelessTest", "stateless-1");
            var repository = new AggregateRepository<StatelessTestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<StatelessTestState, object>(repository, decider, registry);

            // Act: Execute stateless command
            var result = await executor.ExecuteAsync(streamId, new StatelessCommand("data-1"), metadata: []);

            // Assert
            Assert.Single(result.Events);
            Assert.Equal(1, result.Pointer.Version);
            var evt = result.Events[0].Event as StatelessEvent;
            Assert.NotNull(evt);
            Assert.Equal("data-1", evt.Data);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithStatefulCommand_LoadsStateAndValidates()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new StatelessTestStateFolder(registry);
            var decider = new StatelessTestDecider();
            var streamId = new StreamIdentifier("StatelessTest", "stateful-1");
            var repository = new AggregateRepository<StatelessTestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<StatelessTestState, object>(repository, decider, registry);

            // Create initial state
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-1"), metadata: []);

            // Act: Execute stateful command that depends on state
            var result = await executor.ExecuteAsync(streamId, new StatefulCommand(), metadata: []);

            // Assert: Event includes count from loaded state
            Assert.Single(result.Events);
            Assert.Equal(2, result.Pointer.Version);
            var evt = result.Events[0].Event as StatefulEvent;
            Assert.NotNull(evt);
            Assert.Equal(1, evt.StateCount); // State was loaded with count=1
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatelessCommand_DoesNotLoadState()
    {
        // Arrange: Create a stream with existing events
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new StatelessTestStateFolder(registry);
            var trackingDecider = new TrackingStatelessDecider();
            var streamId = new StreamIdentifier("StatelessTest", "no-load-test");
            var repository = new AggregateRepository<StatelessTestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<StatelessTestState, object>(repository, trackingDecider, registry);

            // Create existing state with count=5
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-1"), metadata: []);
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-2"), metadata: []);
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-3"), metadata: []);
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-4"), metadata: []);
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-5"), metadata: []);

            trackingDecider.ReceivedStates.Clear();

            // Act: Execute another stateless command
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-6"), metadata: []);

            // Assert: Decider received initial state (count=0), not loaded state (count=5)
            Assert.Single(trackingDecider.ReceivedStates);
            Assert.Equal(0, trackingDecider.ReceivedStates[0].Count);
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatefulCommand_LoadsCurrentState()
    {
        // Arrange: Create a stream with existing events
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new StatelessTestStateFolder(registry);
            var trackingDecider = new TrackingStatelessDecider();
            var streamId = new StreamIdentifier("StatelessTest", "load-test");
            var repository = new AggregateRepository<StatelessTestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<StatelessTestState, object>(repository, trackingDecider, registry);

            // Create existing state with count=3
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-1"), metadata: []);
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-2"), metadata: []);
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-3"), metadata: []);

            trackingDecider.ReceivedStates.Clear();

            // Act: Execute a stateful command
            await executor.ExecuteAsync(streamId, new StatefulCommand(), metadata: []);

            // Assert: Decider received loaded state (count=3), not initial state (count=0)
            Assert.Single(trackingDecider.ReceivedStates);
            Assert.Equal(3, trackingDecider.ReceivedStates[0].Count);
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatelessCommand_WithoutExpectedVersionKey_AppendsRegardlessOfVersion()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new StatelessTestStateFolder(registry);
            var decider = new StatelessTestDecider();
            var streamId = new StreamIdentifier("StatelessTest", "no-version-test");
            var repository = new AggregateRepository<StatelessTestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<StatelessTestState, object>(repository, decider, registry);

            // Create version 1
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-1"), metadata: []);

            // Act: Execute stateless command without ExpectedVersionKey - should always succeed
            var result = await executor.ExecuteAsync(
                streamId,
                new StatelessCommand("data-2"),
                metadata: []);

            // Assert: Command executed successfully at any current version
            Assert.Single(result.Events);
            Assert.Equal(2, result.Pointer.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatelessCommand_WithExpectedVersionKey_EnforcesVersion()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new StatelessTestStateFolder(registry);
            var decider = new StatelessTestDecider();
            var streamId = new StreamIdentifier("StatelessTest", "version-enforce-stateless-test");
            var repository = new AggregateRepository<StatelessTestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<StatelessTestState, object>(repository, decider, registry);

            // Create version 1
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-1"), metadata: []);

            // Act & Assert: Execute stateless command with wrong expected version - should throw
            await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
            {
                await executor.ExecuteAsync(
                    streamId,
                    new StatelessCommandWithExpectedVersion("data-2"),
                    metadata: [new AppendMetadata("ExpectedVersion", 999L)]);
            });
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatelessCommand_WithExpectedVersionKey_SucceedsWhenVersionMatches()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new StatelessTestStateFolder(registry);
            var decider = new StatelessTestDecider();
            var streamId = new StreamIdentifier("StatelessTest", "version-match-stateless-test");
            var repository = new AggregateRepository<StatelessTestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<StatelessTestState, object>(repository, decider, registry);

            // Create version 1
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-1"), metadata: []);

            // Act: Execute stateless command with correct expected version - should succeed
            var result = await executor.ExecuteAsync(
                streamId,
                new StatelessCommandWithExpectedVersion("data-2"),
                metadata: [new AppendMetadata("ExpectedVersion", 1L)]);

            // Assert: Command executed successfully
            Assert.Single(result.Events);
            Assert.Equal(2, result.Pointer.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatefulCommand_EnforcesExpectedVersion()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new StatelessTestStateFolder(registry);
            var decider = new StatelessTestDecider();
            var streamId = new StreamIdentifier("StatelessTest", "version-enforce-test");
            var repository = new AggregateRepository<StatelessTestState>(eventStore, folder, NoOpSnapshotStore.Instance);
            var executor = new AggregateCommandExecutor<StatelessTestState, object>(repository, decider, registry);

            // Create version 1
            await executor.ExecuteAsync(streamId, new StatelessCommand("data-1"), metadata: []);

            // Act & Assert: Execute stateful command with wrong expected version - should throw
            await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
            {
                await executor.ExecuteAsync(
                    streamId,
                    new StatefulCommandWithExpectedVersion(),
                    metadata: [new AppendMetadata("ExpectedVersion", 999L)]);
            });
        }
    }
}

// Test aggregate
[Aggregate("StatelessTest")]
public sealed record StatelessTestState
{
    public int Count { get; init; }
}

// Test events
[Event("StatelessTest", "StatelessEvent", 1)]
public sealed record StatelessEvent
{
    public string Data { get; init; } = string.Empty;
}

[Event("StatelessTest", "StatefulEvent", 1)]
public sealed record StatefulEvent
{
    public int StateCount { get; init; }
}

// Test commands
[Command("StatelessTest", Stateless = true)]
public sealed record StatelessCommand(string Data);

[Command("StatelessTest", Stateless = true, ExpectedVersionKey = "ExpectedVersion")]
public sealed record StatelessCommandWithExpectedVersion(string Data);

[Command("StatelessTest", Stateless = false)]
public sealed record StatefulCommand;

[Command("StatelessTest", Stateless = false, ExpectedVersionKey = "ExpectedVersion")]
public sealed record StatefulCommandWithExpectedVersion;

// Test state folder
public class StatelessTestStateFolder : StateFolder<StatelessTestState>
{
    public StatelessTestStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override StatelessTestState InitialState() => new() { Count = 0 };

    protected StatelessTestState When(StatelessEvent e, StatelessTestState state)
    {
        return state with { Count = state.Count + 1 };
    }

    protected StatelessTestState When(StatefulEvent e, StatelessTestState state)
    {
        return state with { Count = state.Count + 1 };
    }
}

// Test decider
public class StatelessTestDecider : CommandDecider<StatelessTestState, object>
{
    protected override IReadOnlyList<object> ExecuteCommand(StatelessTestState state, object command)
    {
        return command switch
        {
            StatelessCommand cmd => Event(new StatelessEvent { Data = cmd.Data }),
            StatelessCommandWithExpectedVersion cmd => Event(new StatelessEvent { Data = cmd.Data }),
            StatefulCommand => Event(new StatefulEvent { StateCount = state.Count }),
            StatefulCommandWithExpectedVersion => Event(new StatefulEvent { StateCount = state.Count }),
            _ => throw new InvalidOperationException($"Unknown command type: {command.GetType().Name}")
        };
    }
}

// Test tracking decider
public class TrackingStatelessDecider : CommandDecider<StatelessTestState, object>
{
    public List<StatelessTestState> ReceivedStates { get; } = new();

    protected override IReadOnlyList<object> ExecuteCommand(StatelessTestState state, object command)
    {
        ReceivedStates.Add(state);

        return command switch
        {
            StatelessCommand cmd => Event(new StatelessEvent { Data = cmd.Data }),
            StatefulCommand => Event(new StatefulEvent { StateCount = state.Count }),
            _ => throw new InvalidOperationException($"Unknown command type: {command.GetType().Name}")
        };
    }
}

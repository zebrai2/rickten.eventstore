using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.Projector;
using Rickten.Reactor;
using Rickten.TestUtils;

namespace Rickten.Runtime.Tests;

// Test events
[Event("TestAggregate", "Triggered", 1)]
public record TestTriggeredEvent(string Id, int Value);

[Event("TestAggregate", "Processed", 1)]
public record TestProcessedEvent(string Id, string Reason);

// Test command
[Command("TestAggregate")]
public record ProcessTestCommand(string Id, string Reason);

// Test aggregate state
[Aggregate("TestAggregate")]
public record TestAggregateState(string Id, int ProcessCount);

// Test state folder
public class TestAggregateStateFolder : StateFolder<TestAggregateState>
{
    public TestAggregateStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override TestAggregateState InitialState() => new TestAggregateState("", 0);

    protected TestAggregateState When(TestProcessedEvent e, TestAggregateState state)
    {
        return state with { Id = e.Id, ProcessCount = state.ProcessCount + 1 };
    }
}

// Test command decider
public class TestCommandDecider : CommandDecider<TestAggregateState, ProcessTestCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(TestAggregateState state, ProcessTestCommand command)
    {
        return Event(new TestProcessedEvent(command.Id, command.Reason));
    }
}

// Test projection view
public record TestProjectionView(Dictionary<string, int> Values);

// Test projection
[Rickten.Projector.Projection("TestProjection")]
public class TestProjection : Rickten.Projector.Projection<TestProjectionView>
{
    public override TestProjectionView InitialView() => new TestProjectionView(new Dictionary<string, int>());

    protected override TestProjectionView ApplyEvent(TestProjectionView view, StreamEvent streamEvent)
    {
        if (streamEvent.Event is TestTriggeredEvent evt)
        {
            var values = new Dictionary<string, int>(view.Values)
            {
                [evt.Id] = evt.Value
            };
            return view with { Values = values };
        }
        return view;
    }
}

// Test reaction with polling interval in attribute
[Reaction("TestReaction", EventTypes = new[] { "TestAggregate.Triggered.v1" }, PollingIntervalMilliseconds = 100)]
public class TestReaction : Reaction<TestProjectionView, ProcessTestCommand>
{
    private readonly TestProjection _projection;

    public TestReaction(EventStore.TypeMetadata.ITypeMetadataRegistry registry)
        : base(registry)
    {
        _projection = new TestProjection();
    }

    public override IProjection<TestProjectionView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(TestProjectionView view, StreamEvent trigger)
    {
        if (trigger.Event is TestTriggeredEvent evt)
        {
            yield return new StreamIdentifier("TestAggregate", evt.Id);
        }
    }

    protected override ProcessTestCommand BuildCommand(StreamIdentifier stream, TestProjectionView view, StreamEvent trigger)
    {
        return new ProcessTestCommand(stream.Identifier, "Reacted");
    }
}

// Test reaction without polling interval (uses default)
[Reaction("DefaultIntervalReaction", EventTypes = new[] { "TestAggregate.Triggered.v1" })]
public class DefaultIntervalReaction : Reaction<TestProjectionView, ProcessTestCommand>
{
    private readonly TestProjection _projection;

    public DefaultIntervalReaction(EventStore.TypeMetadata.ITypeMetadataRegistry registry)
        : base(registry)
    {
        _projection = new TestProjection();
    }

    public override IProjection<TestProjectionView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(TestProjectionView view, StreamEvent trigger)
    {
        if (trigger.Event is TestTriggeredEvent evt)
        {
            yield return new StreamIdentifier("TestAggregate", evt.Id);
        }
    }

    protected override ProcessTestCommand BuildCommand(StreamIdentifier stream, TestProjectionView view, StreamEvent trigger)
    {
        return new ProcessTestCommand(stream.Identifier, "DefaultReacted");
    }
}

// In-memory projection store for testing
public class InMemoryProjectionStore : IProjectionStore
{
    private readonly Dictionary<(string Namespace, string Key), object> _projections = new();

    public Task<Rickten.EventStore.Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        CancellationToken cancellationToken = default)
    {
        return LoadProjectionAsync<TState>(projectionKey, "system", cancellationToken);
    }

    public Task<Rickten.EventStore.Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        var key = (@namespace, projectionKey);
        if (_projections.TryGetValue(key, out var stored) && stored is Rickten.EventStore.Projection<TState> projection)
        {
            return Task.FromResult<Rickten.EventStore.Projection<TState>?>(projection);
        }
        return Task.FromResult<Rickten.EventStore.Projection<TState>?>(null);
    }

    public Task SaveProjectionAsync<TState>(
        string projectionKey,
        long globalPosition,
        TState state,
        CancellationToken cancellationToken = default)
    {
        return SaveProjectionAsync(projectionKey, globalPosition, state, "system", cancellationToken);
    }

    public Task SaveProjectionAsync<TState>(
        string projectionKey,
        long globalPosition,
        TState state,
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        var key = (@namespace, projectionKey);
        _projections[key] = new Rickten.EventStore.Projection<TState>(state, globalPosition);
        return Task.CompletedTask;
    }
}

public class ReactionHostedServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IServiceProvider _serviceProvider;
    private readonly InMemoryProjectionStore _projectionStore;

    public ReactionHostedServiceTests()
    {
        (_connection, _serviceProvider) = TestServiceFactory.CreateServiceProvider();
        _projectionStore = new InMemoryProjectionStore();
    }

    [Fact]
    public async Task HostedService_Processes_Reaction_On_Interval()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();

        // Append trigger event FIRST
        var stream = new StreamIdentifier("TestAggregate", "test-1");
        await eventStore.AppendAsync(new StreamPointer(stream, 0), new List<AppendEvent>
        {
            new AppendEvent(new TestTriggeredEvent("test-1", 42), null)
        });

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(_connection);
        services.AddSingleton<IEventStore>(eventStore);
        services.AddSingleton<IProjectionStore>(_projectionStore);
        services.AddSingleton(_serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());

        services.AddReactions(typeof(TestReaction).Assembly);

        // Register executor dependencies
        services.AddTransient<TestAggregateStateFolder>();
        services.AddTransient<TestCommandDecider>();
        services.AddTransient<AggregateRepository<TestAggregateState>>(sp =>
            new AggregateRepository<TestAggregateState>(
                sp.GetRequiredService<IEventStore>(),
                sp.GetRequiredService<TestAggregateStateFolder>(),
                NoOpSnapshotStore.Instance));

        services.AddTransient<AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>>(sp =>
            new AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>(
                sp.GetRequiredService<AggregateRepository<TestAggregateState>>(),
                sp.GetRequiredService<TestCommandDecider>(),
                sp.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>()));

        services.AddRicktenRuntime(options =>
        {
            options.DefaultPollingInterval = TimeSpan.FromSeconds(10);
        });

        // Uses attribute polling interval (100ms)
        services.AddHostedReaction<TestReaction, TestAggregateState, TestProjectionView, ProcessTestCommand>();

        var provider = services.BuildServiceProvider();

        // Act - Start the host
        var cts = new CancellationTokenSource();
        var host = provider.GetRequiredService<IHostedService>();
        await host.StartAsync(cts.Token);

        // Wait for reaction to process (attribute specifies 100ms interval)
        await Task.Delay(500);

        // Stop the host
        cts.Cancel();
        await host.StopAsync(CancellationToken.None);

        // Assert - Check that command was executed
        var events = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(stream, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Should have both trigger event AND processed event
        Assert.Contains(events, e => e.Event is TestTriggeredEvent);
        Assert.Contains(events, e => e.Event is TestProcessedEvent);
    }

    [Fact]
    public async Task HostedService_Uses_Default_Interval_When_Attribute_Not_Set()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();

        var stream = new StreamIdentifier("TestAggregate", "test-2");
        await eventStore.AppendAsync(new StreamPointer(stream, 0), new List<AppendEvent>
        {
            new AppendEvent(new TestTriggeredEvent("test-2", 99), null)
        });

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(_connection);
        services.AddSingleton<IEventStore>(eventStore);
        services.AddSingleton<IProjectionStore>(_projectionStore);
        services.AddSingleton(_serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());

        services.AddReactions(typeof(DefaultIntervalReaction).Assembly);

        services.AddTransient<TestAggregateStateFolder>();
        services.AddTransient<TestCommandDecider>();
        services.AddTransient<AggregateRepository<TestAggregateState>>(sp =>
            new AggregateRepository<TestAggregateState>(
                sp.GetRequiredService<IEventStore>(),
                sp.GetRequiredService<TestAggregateStateFolder>(),
                NoOpSnapshotStore.Instance));

        services.AddTransient<AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>>(sp =>
            new AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>(
                sp.GetRequiredService<AggregateRepository<TestAggregateState>>(),
                sp.GetRequiredService<TestCommandDecider>(),
                sp.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>()));

        services.AddRicktenRuntime(options =>
        {
            options.DefaultPollingInterval = TimeSpan.FromMilliseconds(100);
        });

        // Reaction has no polling interval in attribute, should use default
        services.AddHostedReaction<DefaultIntervalReaction, TestAggregateState, TestProjectionView, ProcessTestCommand>();

        var provider = services.BuildServiceProvider();

        var cts = new CancellationTokenSource();
        var host = provider.GetRequiredService<IHostedService>();
        await host.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await host.StopAsync(CancellationToken.None);

        // Assert
        var events = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(stream, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e.Event is TestProcessedEvent tpe && tpe.Reason == "DefaultReacted");
    }

    [Fact]
    public async Task HostedService_Parameter_Override_Takes_Precedence()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();

        var stream = new StreamIdentifier("TestAggregate", "test-3");
        await eventStore.AppendAsync(new StreamPointer(stream, 0), new List<AppendEvent>
        {
            new AppendEvent(new TestTriggeredEvent("test-3", 77), null)
        });

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(_connection);
        services.AddSingleton<IEventStore>(eventStore);
        services.AddSingleton<IProjectionStore>(_projectionStore);
        services.AddSingleton(_serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());

        services.AddReactions(typeof(TestReaction).Assembly);

        services.AddTransient<TestAggregateStateFolder>();
        services.AddTransient<TestCommandDecider>();
        services.AddTransient<AggregateRepository<TestAggregateState>>(sp =>
            new AggregateRepository<TestAggregateState>(
                sp.GetRequiredService<IEventStore>(),
                sp.GetRequiredService<TestAggregateStateFolder>(),
                NoOpSnapshotStore.Instance));

        services.AddTransient<AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>>(sp =>
            new AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>(
                sp.GetRequiredService<AggregateRepository<TestAggregateState>>(),
                sp.GetRequiredService<TestCommandDecider>(),
                sp.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>()));

        services.AddRicktenRuntime(options =>
        {
            options.DefaultPollingInterval = TimeSpan.FromSeconds(10);
        });

        // Override attribute interval (100ms) with parameter (50ms)
        services.AddHostedReaction<TestReaction, TestAggregateState, TestProjectionView, ProcessTestCommand>(
            pollingInterval: TimeSpan.FromMilliseconds(50));

        var provider = services.BuildServiceProvider();

        var cts = new CancellationTokenSource();
        var host = provider.GetRequiredService<IHostedService>();
        await host.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await host.StopAsync(CancellationToken.None);

        // Assert
        var events = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(stream, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e.Event is TestProcessedEvent);
    }

    [Fact]
    public async Task HostedService_Stops_Gracefully_On_Cancellation()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(_connection);
        services.AddSingleton<IEventStore>(_serviceProvider.GetRequiredService<IEventStore>());
        services.AddSingleton<IProjectionStore>(_projectionStore);
        services.AddSingleton(_serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());

        services.AddReactions(typeof(TestReaction).Assembly);

        services.AddTransient<TestAggregateStateFolder>();
        services.AddTransient<TestCommandDecider>();
        services.AddTransient<AggregateRepository<TestAggregateState>>(sp =>
            new AggregateRepository<TestAggregateState>(
                sp.GetRequiredService<IEventStore>(),
                sp.GetRequiredService<TestAggregateStateFolder>(),
                NoOpSnapshotStore.Instance));

        services.AddTransient<AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>>(sp =>
            new AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>(
                sp.GetRequiredService<AggregateRepository<TestAggregateState>>(),
                sp.GetRequiredService<TestCommandDecider>(),
                sp.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>()));

        services.AddRicktenRuntime(options =>
        {
            options.DefaultPollingInterval = TimeSpan.FromMilliseconds(100);
        });

        services.AddHostedReaction<TestReaction, TestAggregateState, TestProjectionView, ProcessTestCommand>();

        var provider = services.BuildServiceProvider();

        var cts = new CancellationTokenSource();
        var host = provider.GetRequiredService<IHostedService>();

        // Act - Start and immediately stop
        await host.StartAsync(cts.Token);
        cts.Cancel();

        // Should complete without hanging or throwing
        await host.StopAsync(CancellationToken.None);

        // Assert - If we got here, graceful shutdown worked
        Assert.True(true);
    }

    [Fact]
    public void AddHostedReaction_Registers_Service()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton<IEventStore>(_serviceProvider.GetRequiredService<IEventStore>());
        services.AddSingleton<IProjectionStore>(_projectionStore);
        services.AddSingleton(_serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());

        services.AddReactions(typeof(TestReaction).Assembly);

        services.AddTransient<TestAggregateStateFolder>();
        services.AddTransient<TestCommandDecider>();
        services.AddTransient<AggregateRepository<TestAggregateState>>(sp =>
            new AggregateRepository<TestAggregateState>(
                sp.GetRequiredService<IEventStore>(),
                sp.GetRequiredService<TestAggregateStateFolder>(),
                NoOpSnapshotStore.Instance));

        services.AddTransient<AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>>(sp =>
            new AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>(
                sp.GetRequiredService<AggregateRepository<TestAggregateState>>(),
                sp.GetRequiredService<TestCommandDecider>(),
                sp.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>()));

        services.AddRicktenRuntime();
        services.AddHostedReaction<TestReaction, TestAggregateState, TestProjectionView, ProcessTestCommand>(
            pollingInterval: TimeSpan.FromSeconds(1));

        // Act
        var provider = services.BuildServiceProvider();

        // Assert - Can resolve hosted service
        var hostedServices = provider.GetServices<IHostedService>();
        Assert.NotEmpty(hostedServices);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }
}

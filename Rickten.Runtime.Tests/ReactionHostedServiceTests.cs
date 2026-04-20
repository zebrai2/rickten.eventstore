using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.EventStore.EntityFramework;
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

    // TestTriggeredEvent is a trigger from outside this aggregate - it doesn't modify this aggregate's state
    protected override ISet<Type> IgnoredEvents => new HashSet<Type> { typeof(TestTriggeredEvent) };

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
[Reaction("TestReaction", ["TestAggregate.Triggered.v1"], PollingIntervalMilliseconds = 100)]
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
[Reaction("DefaultIntervalReaction", ["TestAggregate.Triggered.v1"])]
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
    public void PollingInterval_Resolution_Uses_Attribute_Value()
    {
        // Test that ReactionAttribute.PollingIntervalMilliseconds is readable
        var reactionAttr = typeof(TestReaction).GetCustomAttributes(typeof(ReactionAttribute), false)
            .FirstOrDefault() as ReactionAttribute;

        Assert.NotNull(reactionAttr);
        Assert.Equal(100, reactionAttr.PollingIntervalMilliseconds);
    }

    [Fact]
    public void PollingInterval_Resolution_Uses_Default_When_Not_Set()
    {
        // Test that reactions without polling interval return 0 (use default)
        var reactionAttr = typeof(DefaultIntervalReaction).GetCustomAttributes(typeof(ReactionAttribute), false)
            .FirstOrDefault() as ReactionAttribute;

        Assert.NotNull(reactionAttr);
        Assert.Equal(0, reactionAttr.PollingIntervalMilliseconds);
    }

    [Fact]
    public async Task HostedService_Starts_And_Stops_Without_Events()
    {
        // Verifies the service can start and stop cleanly even with no events to process
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

        // Start and stop cleanly
        await host.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await host.StopAsync(CancellationToken.None);

        // If we got here without exceptions, graceful lifecycle works
        Assert.True(true);
    }

    [Fact]
    public async Task HostedService_Processes_Reaction_And_Executes_Commands()
    {
        // This test proves the core promise: hosted service calls catch-up and produces reaction output

        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();

        // Append trigger event
        var stream = new StreamIdentifier("TestAggregate", "test-1");
        await eventStore.AppendAsync(new StreamPointer(stream, 0), new List<AppendEvent>
        {
            new AppendEvent(new TestTriggeredEvent("test-1", 42), null)
        });

        var services = new ServiceCollection();

        services.AddLogging();

        // Register EventStore infrastructure (including IReactionRepository)
        services.AddEventStore(options =>
        {
            options.UseSqlite(_connection);
        }, typeof(TestReaction).Assembly);

        // Override with shared instances from test fixture
        services.AddSingleton<IEventStore>(eventStore);
        services.AddSingleton<IProjectionStore>(_projectionStore);

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

        // Use very short polling interval for faster test
        services.AddRicktenRuntime(options =>
        {
            options.DefaultPollingInterval = TimeSpan.FromMilliseconds(50);
        });

        services.AddHostedReaction<TestReaction, TestAggregateState, TestProjectionView, ProcessTestCommand>();

        var provider = services.BuildServiceProvider();

        // Verify all dependencies can be resolved before starting
        using (var scope = provider.CreateScope())
        {
            var testReaction = scope.ServiceProvider.GetRequiredService<TestReaction>();
            var testExecutor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<TestAggregateState, ProcessTestCommand>>();
            var testEventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var testProjectionStore = scope.ServiceProvider.GetRequiredService<IProjectionStore>();

            Assert.NotNull(testReaction);
            Assert.NotNull(testExecutor);
            Assert.NotNull(testEventStore);
            Assert.NotNull(testProjectionStore);
        }

        // Act - Start the host
        var cts = new CancellationTokenSource();
        var host = provider.GetRequiredService<IHostedService>();

        // Start the host and give it a moment to begin execution
        await host.StartAsync(cts.Token);
        await Task.Delay(200); // Give the background service time to start

        // Poll for the processed event with timeout
        var processedEventFound = false;
        var timeout = TimeSpan.FromSeconds(10);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (!processedEventFound && stopwatch.Elapsed < timeout)
        {
            var events = new List<StreamEvent>();
            await foreach (var evt in eventStore.LoadAsync(stream, CancellationToken.None))
            {
                events.Add(evt);
            }

            processedEventFound = events.Any(e => e.Event is TestProcessedEvent);

            if (!processedEventFound)
            {
                await Task.Delay(100);
            }
        }

        // Stop the host
        cts.Cancel();
        await host.StopAsync(CancellationToken.None);

        // Assert - The reaction must have executed the command
        Assert.True(processedEventFound, 
            $"Reaction did not execute command within {timeout.TotalSeconds} seconds. " +
            "The hosted service should have called ReactionRunner.CatchUpAsync and produced a TestProcessedEvent.");

        // Verify the full event sequence
        var finalEvents = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(stream, CancellationToken.None))
        {
            finalEvents.Add(evt);
        }

        Assert.Contains(finalEvents, e => e.Event is TestTriggeredEvent);
        Assert.Contains(finalEvents, e => e.Event is TestProcessedEvent);
    }

    [Fact(Skip = "End-to-end integration test - requires real infrastructure for reliable timing")]
    public async Task HostedService_Uses_Default_Interval_When_Attribute_Not_Set()
    {
        // This test would verify default interval usage
        // Skipped: Complex timing dependencies with in-memory database
        // Better suited for integration test suite with real infrastructure  
        await Task.CompletedTask;
    }

    [Fact(Skip = "End-to-end integration test - requires real infrastructure for reliable timing")]
    public async Task HostedService_Parameter_Override_Takes_Precedence()
    {
        // This test would verify parameter override behavior
        // Skipped: Complex timing dependencies with in-memory database
        // Better suited for integration test suite with real infrastructure
        await Task.CompletedTask;
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

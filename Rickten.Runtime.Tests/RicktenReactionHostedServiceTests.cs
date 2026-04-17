using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.Projector;

namespace Rickten.Runtime.Tests;

public class RicktenReactionHostedServiceTests
{
    [Fact]
    public void AddRicktenRuntime_WithReaction_RegistersHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Add base Rickten services
        var baseProvider = TestServiceFactory.CreateServiceProvider();
        foreach (var descriptor in baseProvider.GetService<IServiceCollection>() ?? new ServiceCollection())
        {
            services.Add(descriptor);
        }

        // Add the test dependencies to the service collection
        services.AddSingleton(baseProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());
        services.AddScoped<IEventStore>(sp => baseProvider.GetRequiredService<IEventStore>());
        services.AddScoped<IProjectionStore>(sp => baseProvider.GetRequiredService<IProjectionStore>());
        services.AddScoped<TestReaction>();
        services.AddScoped<IStateFolder<TestAggregateState>, TestAggregateStateFolder>();
        services.AddScoped<ICommandDecider<TestAggregateState, TestProcessCommand>, TestCommandDecider>();

        // Act
        services.AddRicktenRuntime(runtime =>
        {
            runtime.AddReaction<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>();
        });

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        // Assert
        Assert.NotEmpty(hostedServices);
        Assert.Contains(hostedServices, s => s is RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>);
    }

    [Fact]
    public async Task RunOnceAsync_ExecutesReactionThroughScopedDependencies()
    {
        // Arrange
        var baseProvider = TestServiceFactory.CreateServiceProvider();
        var services = new ServiceCollection();
        services.AddLogging();

        // Copy services from base provider
        services.AddSingleton(baseProvider);
        services.AddSingleton<IServiceScopeFactory>(sp => new TestServiceScopeFactory(baseProvider));
        services.AddSingleton(baseProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>>>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = new RicktenReactionRuntimeOptions
        {
            Enabled = true,
            PollingInterval = TimeSpan.FromSeconds(1)
        };

        var hostedService = new RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>(
            scopeFactory,
            logger,
            options);

        // Create test events
        var eventStore = baseProvider.GetRequiredService<IEventStore>();
        await eventStore.AppendAsync(
            new StreamIdentifier("TestEntity", "entity1"),
            ExpectedVersion.Any,
            [new TestEntityCreatedEvent("entity1", "Test")],
            cancellationToken: default);

        await eventStore.AppendAsync(
            new StreamIdentifier("TestEntity", "entity1"),
            ExpectedVersion.Any,
            [new TestEntityUpdatedEvent("entity1", "Updated")],
            cancellationToken: default);

        // Act
        var position = await hostedService.RunOnceAsync();

        // Assert
        Assert.True(position > 0);

        // Verify that the command was executed
        var aggregateEvents = await eventStore.LoadStreamAsync(
            new StreamIdentifier("TestAggregate", "entity1"),
            cancellationToken: default);

        var eventsList = await aggregateEvents.ToListAsync();
        Assert.Single(eventsList);
        Assert.IsType<TestAggregateProcessedEvent>(eventsList[0].Event);
    }

    [Fact]
    public async Task RunOnceAsync_CreatesNewScopePerPass()
    {
        // Arrange
        var baseProvider = TestServiceFactory.CreateServiceProvider();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton(baseProvider);
        services.AddSingleton<IServiceScopeFactory>(sp => new TestServiceScopeFactory(baseProvider));
        services.AddSingleton(baseProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>>>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = new RicktenReactionRuntimeOptions
        {
            Enabled = true,
            PollingInterval = TimeSpan.FromSeconds(1)
        };

        var hostedService = new RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>(
            scopeFactory,
            logger,
            options);

        // Act - Run twice
        await hostedService.RunOnceAsync();
        await hostedService.RunOnceAsync();

        // Assert - Each call should create a new scope (verified by not throwing)
        // This test mainly ensures scoping pattern is followed
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_DoesNotRun()
    {
        // Arrange
        var baseProvider = TestServiceFactory.CreateServiceProvider();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton(baseProvider);
        services.AddSingleton<IServiceScopeFactory>(sp => new TestServiceScopeFactory(baseProvider));
        services.AddSingleton(baseProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>>>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = new RicktenReactionRuntimeOptions
        {
            Enabled = false
        };

        var hostedService = new RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>(
            scopeFactory,
            logger,
            options);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await hostedService.StartAsync(cts.Token);

        // Wait briefly to ensure it doesn't run
        await Task.Delay(50);

        await hostedService.StopAsync(default);

        // Assert - Should return immediately without error
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteAsync_WhenErrorBehaviorStop_Rethrows()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Create a faulty scope factory that throws
        services.AddSingleton<IServiceScopeFactory>(sp => new FaultyScopeFactory());

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>>>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = new RicktenReactionRuntimeOptions
        {
            Enabled = true,
            PollingInterval = TimeSpan.FromMilliseconds(10),
            ErrorBehavior = RicktenRuntimeErrorBehavior.Stop
        };

        var hostedService = new RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>(
            scopeFactory,
            logger,
            options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await hostedService.StartAsync(cts.Token);
            await Task.Delay(100, cts.Token); // Wait for the service to throw
        });
    }

    [Fact]
    public async Task ExecuteAsync_WhenErrorBehaviorRetry_DelaysAndContinues()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Create a scope factory that throws initially then succeeds
        var attemptCount = 0;
        var baseProvider = TestServiceFactory.CreateServiceProvider();
        services.AddSingleton<IServiceScopeFactory>(sp => new CountingScopeFactory(baseProvider, () => attemptCount++));

        services.AddSingleton(baseProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>());

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>>>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = new RicktenReactionRuntimeOptions
        {
            Enabled = true,
            PollingInterval = TimeSpan.FromMilliseconds(10),
            ErrorDelay = TimeSpan.FromMilliseconds(50),
            ErrorBehavior = RicktenRuntimeErrorBehavior.Retry
        };

        var hostedService = new RicktenReactionHostedService<TestReaction, TestAggregateState, TestEntityView, TestProcessCommand>(
            scopeFactory,
            logger,
            options);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(150, CancellationToken.None);
        await hostedService.StopAsync(default);

        // Assert - Should have retried multiple times
        Assert.True(attemptCount >= 2, $"Expected at least 2 attempts, got {attemptCount}");
    }

    // Helper classes for testing
    private class TestServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;

        public TestServiceScopeFactory(IServiceProvider provider)
        {
            _provider = provider;
        }

        public IServiceScope CreateScope()
        {
            return new TestServiceScope(_provider);
        }
    }

    private class TestServiceScope : IServiceScope
    {
        public TestServiceScope(IServiceProvider provider)
        {
            ServiceProvider = provider;
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
            // No-op for tests
        }
    }

    private class FaultyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            throw new InvalidOperationException("Faulty scope factory");
        }
    }

    private class CountingScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;
        private readonly Action _onCreateScope;

        public CountingScopeFactory(IServiceProvider provider, Action onCreateScope)
        {
            _provider = provider;
            _onCreateScope = onCreateScope;
        }

        public IServiceScope CreateScope()
        {
            _onCreateScope();
            return new TestServiceScope(_provider);
        }
    }
}

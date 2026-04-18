using Rickten.EventStore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Rickten.Aggregator.Tests;

/// <summary>
/// Tests for metadata-based expected version command execution.
/// Covers CQRS stale-read protection scenarios.
/// </summary>
public class CommandVersionModeTests
{
    [Fact]
    public void CommandAttribute_DefaultExpectedVersionKey_IsNull()
    {
        // Arrange & Act
        var attribute = new CommandAttribute("TestAggregate");

        // Assert
        Assert.Null(attribute.ExpectedVersionKey);
    }

    [Fact]
    public void CommandAttribute_CanSetExpectedVersionKey()
    {
        // Arrange & Act
        var attribute = new CommandAttribute("TestAggregate")
        {
            ExpectedVersionKey = "ExpectedVersion"
        };

        // Assert
        Assert.Equal("ExpectedVersion", attribute.ExpectedVersionKey);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutExpectedVersionKey_ExecutesAgainstLatestState()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "latest-version-test");

            // Act: Execute command without ExpectedVersionKey (default behavior)
            var result = await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Assert
            Assert.Single(result.Events);
            Assert.Equal(1, result.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithExpectedVersionKey_SucceedsWhenVersionMatches()
    {
        // Arrange: Create stream with initial state
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "expected-version-test");

            // Create initial version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act: Execute with expected version in metadata
            var result = await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new ExpectedVersionCommand("order-1"),
                registry,
                metadata: [new AppendMetadata("ExpectedVersion", 1L)]);

            // Assert
            Assert.Single(result.Events);
            Assert.Equal(2, result.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithExpectedVersionMismatch_ThrowsStreamVersionConflictException()
    {
        // Arrange: Create stream and then change it
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "version-mismatch-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Advance to version 2
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act & Assert: Try to execute with stale expected version 1
            var exception = await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
            {
                await StateRunner.ExecuteAsync(
                    eventStore,
                    folder,
                    decider,
                    streamId,
                    new ExpectedVersionCommand("order-1"),
                    registry,
                    metadata: [new AppendMetadata("ExpectedVersion", 1L)]);
            });

            Assert.Equal(1, exception.ExpectedVersion.Version);
            Assert.Equal(2, exception.ActualVersion.Version);
            Assert.Equal(streamId, exception.ExpectedVersion.Stream);
            Assert.Equal(streamId, exception.ActualVersion.Stream);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingMetadata_ThrowsInvalidOperationException()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "missing-metadata-test");

            // Act & Assert: Command has ExpectedVersionKey but metadata is missing
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await StateRunner.ExecuteAsync(
                    eventStore,
                    folder,
                    decider,
                    streamId,
                    new ExpectedVersionCommand("order-1"),
                    registry);
            });

            Assert.Contains("ExpectedVersion", exception.Message);
            Assert.Contains("not provided", exception.Message);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidMetadataValue_ThrowsInvalidOperationException()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "invalid-metadata-test");

            // Act & Assert: Metadata value cannot be converted to long
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await StateRunner.ExecuteAsync(
                    eventStore,
                    folder,
                    decider,
                    streamId,
                    new ExpectedVersionCommand("order-1"),
                    registry,
                    metadata: [new AppendMetadata("ExpectedVersion", "not-a-number")]);
            });

            Assert.Contains("cannot be converted to long", exception.Message);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithMetadataInt_ConvertsToLong()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "int-metadata-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act: Pass int instead of long
            var result = await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new ExpectedVersionCommand("order-1"),
                registry,
                metadata: [new AppendMetadata("ExpectedVersion", 1)]);

            // Assert
            Assert.Single(result.Events);
            Assert.Equal(2, result.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithMetadataString_ParsesAsLong()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "string-metadata-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act: Pass string that can be parsed as long
            var result = await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new ExpectedVersionCommand("order-1"),
                registry,
                metadata: [new AppendMetadata("ExpectedVersion", "1")]);

            // Assert
            Assert.Single(result.Events);
            Assert.Equal(2, result.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_IdempotentCommandWithExpectedVersion_ReturnsSuccess()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "idempotent-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act: Execute idempotent command at expected version
            var result = await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new IdempotentExpectedVersionCommand("order-1"),
                registry,
                metadata: [new AppendMetadata("ExpectedVersion", 1L)]);

            // Assert: No events produced, but no error
            Assert.Empty(result.Events);
            Assert.Equal(1, result.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithSnapshot_StillValidatesExpectedVersion()
    {
        // Arrange: Create stream with snapshot
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "snapshot-validation-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Save snapshot at version 1
            var snapshotPointer = new StreamPointer(streamId, 1);
            await snapshotStore.SaveSnapshotAsync(snapshotPointer, new MetadataVersionTestState(1));

            // Advance to version 2
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act & Assert: Expected version check happens with snapshot loading
            var exception = await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
            {
                await StateRunner.ExecuteAsync(
                    eventStore,
                    folder,
                    decider,
                    streamId,
                    new ExpectedVersionCommand("order-1"),
                    registry,
                    snapshotStore,
                    metadata: [new AppendMetadata("ExpectedVersion", 1L)]);
            });

            Assert.Equal(1, exception.ExpectedVersion.Version);
            Assert.Equal(2, exception.ActualVersion.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRunDeciderOnVersionMismatch()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var trackingDecider = new TrackingDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "decider-not-run-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, trackingDecider, streamId, new LatestVersionCommand(), registry);

            // Reset tracking
            trackingDecider.ExecutedCommands.Clear();

            // Act & Assert: Version mismatch should fail before decider runs
            await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
            {
                await StateRunner.ExecuteAsync(
                    eventStore,
                    folder,
                    trackingDecider,
                    streamId,
                    new ExpectedVersionCommand("order-1"),
                    registry,
                    metadata: [new AppendMetadata("ExpectedVersion", 0L)]);
            });

            // Assert: Decider was never called
            Assert.Empty(trackingDecider.ExecutedCommands);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithNullMetadataValue_ThrowsInvalidOperationException()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "null-metadata-test");

            // Act & Assert: Metadata value is null
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await StateRunner.ExecuteAsync(
                    eventStore,
                    folder,
                    decider,
                    streamId,
                    new ExpectedVersionCommand("order-1"),
                    registry,
                    metadata: [new AppendMetadata("ExpectedVersion", null)]);
            });

            Assert.Contains("value was null", exception.Message);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithMetadataShort_ConvertsToLong()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "short-metadata-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act: Pass short instead of long
            var result = await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new ExpectedVersionCommand("order-1"),
                registry,
                metadata: [new AppendMetadata("ExpectedVersion", (short)1)]);

            // Assert
            Assert.Single(result.Events);
            Assert.Equal(2, result.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithExpectedVersionKey_DoesNotPersistExpectedVersionInEventMetadata()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new MetadataVersionTestStateFolder(registry);
            var decider = new MetadataVersionTestDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "metadata-filtering-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act: Execute with expected version + additional metadata
            var result = await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new ExpectedVersionCommand("order-1"),
                registry,
                metadata:
                [
                    new AppendMetadata("ExpectedVersion", 1L),
                    new AppendMetadata("CorrelationId", "correlation-123"),
                    new AppendMetadata("UserId", "user-456")
                ]);

            // Assert: Event was appended successfully
            Assert.Single(result.Events);
            Assert.Equal(2, result.Version);

            // Load events from store to verify metadata
            var loadedEvents = new List<StreamEvent>();
            await foreach (var e in eventStore.LoadAsync(new StreamPointer(streamId, 0)))
                loadedEvents.Add(e);

            // Should have 2 events (version 1 and version 2)
            Assert.Equal(2, loadedEvents.Count);

            // Check the second event (the one we just appended with metadata)
            var persistedEvent = loadedEvents[1];

            // Verify: ExpectedVersion key NOT in persisted metadata
            var expectedVersionMetadata = persistedEvent.Metadata
                .FirstOrDefault(m => m.Key == "ExpectedVersion");
            Assert.Null(expectedVersionMetadata);

            // Verify: Other metadata WAS persisted
            var correlationIdMetadata = persistedEvent.Metadata
                .FirstOrDefault(m => m.Key == "CorrelationId");
            Assert.NotNull(correlationIdMetadata);
            Assert.Equal("correlation-123", correlationIdMetadata.Value?.ToString());

            var userIdMetadata = persistedEvent.Metadata
                .FirstOrDefault(m => m.Key == "UserId");
            Assert.NotNull(userIdMetadata);
            Assert.Equal("user-456", userIdMetadata.Value?.ToString());
        }
    }

    [Fact]
    public async Task ExecuteAsync_CommandWithAttributeButNotInRegistry_ThrowsInvalidOperationException()
    {
        // Arrange: Create registry that returns null for the command type (simulates misconfiguration)
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var fullRegistry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();

            // Create a filtering registry that excludes UnregisteredExpectedVersionCommand
            var filteringRegistry = new FilteringTypeMetadataRegistry(
                fullRegistry,
                excludeType: typeof(UnregisteredExpectedVersionCommand));

            var folder = new MetadataVersionTestStateFolder(filteringRegistry);
            var trackingDecider = new TrackingDecider();
            var streamId = new StreamIdentifier("MetadataVersionTest", "unregistered-command-test");

            // Create a command that has the attribute but will be filtered from registry
            var unregisteredCommand = new UnregisteredExpectedVersionCommand("test-order");

            // Act & Assert: Should throw because command has attribute but isn't in the filtering registry
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await StateRunner.ExecuteAsync(
                    eventStore,
                    folder,
                    trackingDecider,
                    streamId,
                    unregisteredCommand,
                    filteringRegistry,
                    metadata: [new AppendMetadata("ExpectedVersion", 0L)]);
            });

            // Verify the exception explains this is a CRITICAL configuration error
            Assert.Contains("CRITICAL CONFIGURATION ERROR", exception.Message);
            Assert.Contains("UnregisteredExpectedVersionCommand", exception.Message);
            Assert.Contains("is not registered in the type metadata registry", exception.Message);
            Assert.Contains("fatal setup error", exception.Message);
            Assert.Contains("must be fixed immediately", exception.Message);

            // Verify decider was never called (exception thrown before execution)
            Assert.Empty(trackingDecider.ExecutedCommands);
        }
    }
}

// Test domain for metadata-based version tests
[Aggregate("MetadataVersionTest")]
public record MetadataVersionTestState(int Count = 0);

[Event("MetadataVersionTest", "Event", 1)]
public record MetadataVersionTestEvent;

[Command("MetadataVersionTest")]
public record LatestVersionCommand;

[Command("MetadataVersionTest", ExpectedVersionKey = "ExpectedVersion")]
public record ExpectedVersionCommand(string OrderId);

[Command("MetadataVersionTest", ExpectedVersionKey = "ExpectedVersion")]
public record IdempotentExpectedVersionCommand(string OrderId);

// This command has [Command] attribute with ExpectedVersionKey but is NOT registered in TestServiceFactory
// Used to test detection of misconfigured registry
[Command("MetadataVersionTest", ExpectedVersionKey = "ExpectedVersion")]
public record UnregisteredExpectedVersionCommand(string OrderId);

public class MetadataVersionTestStateFolder : StateFolder<MetadataVersionTestState>
{
    public MetadataVersionTestStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override MetadataVersionTestState InitialState() => new(0);

    protected MetadataVersionTestState When(MetadataVersionTestEvent e, MetadataVersionTestState state)
    {
        return state with { Count = state.Count + 1 };
    }
}

public class MetadataVersionTestDecider : CommandDecider<MetadataVersionTestState, object>
{
    protected override IReadOnlyList<object> ExecuteCommand(MetadataVersionTestState state, object command)
    {
        return command switch
        {
            LatestVersionCommand => Event(new MetadataVersionTestEvent()),
            ExpectedVersionCommand => Event(new MetadataVersionTestEvent()),
            IdempotentExpectedVersionCommand => NoEvents(),
            _ => throw new InvalidOperationException($"Unknown command type: {command.GetType().Name}")
        };
    }
}

public class TrackingDecider : CommandDecider<MetadataVersionTestState, object>
{
    public List<object> ExecutedCommands { get; } = new();

    protected override IReadOnlyList<object> ExecuteCommand(MetadataVersionTestState state, object command)
    {
        ExecutedCommands.Add(command);
        return Event(new MetadataVersionTestEvent());
    }
}

/// <summary>
/// Test helper that wraps a registry and filters out a specific type.
/// Used to simulate misconfigured registry scenarios.
/// </summary>
internal class FilteringTypeMetadataRegistry : EventStore.TypeMetadata.ITypeMetadataRegistry
{
    private readonly EventStore.TypeMetadata.ITypeMetadataRegistry _inner;
    private readonly Type _excludeType;

    public FilteringTypeMetadataRegistry(EventStore.TypeMetadata.ITypeMetadataRegistry inner, Type excludeType)
    {
        _inner = inner;
        _excludeType = excludeType;
    }

    public EventStore.TypeMetadata.TypeMetadata? GetMetadataByType(Type type)
    {
        // Return null for the excluded type to simulate it not being registered
        if (type == _excludeType)
        {
            return null;
        }
        return _inner.GetMetadataByType(type);
    }

    public Type? GetTypeByWireName(string wireName)
    {
        return _inner.GetTypeByWireName(wireName);
    }

    public IReadOnlyCollection<Type> GetEventTypesForAggregate(string aggregateName)
    {
        return _inner.GetEventTypesForAggregate(aggregateName);
    }

    public void ValidateEventsForStream(IEnumerable<object> events, string expectedStreamType)
    {
        _inner.ValidateEventsForStream(events, expectedStreamType);
    }
}

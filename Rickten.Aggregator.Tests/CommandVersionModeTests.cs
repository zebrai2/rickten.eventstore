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
    public async Task ExecuteAtVersionAsync_WithMatchingVersion_SuccessfullyExecutes()
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
            var streamId = new StreamIdentifier("MetadataVersionTest", "explicit-version-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act: Use explicit ExecuteAtVersionAsync
            var result = await StateRunner.ExecuteAtVersionAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new LatestVersionCommand(),
                expectedVersion: 1);

            // Assert
            Assert.Single(result.Events);
            Assert.Equal(2, result.Version);
        }
    }

    [Fact]
    public async Task ExecuteAtVersionAsync_WithVersionMismatch_ThrowsStreamVersionConflictException()
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
            var streamId = new StreamIdentifier("MetadataVersionTest", "explicit-mismatch-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Advance to version 2
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act & Assert: Try with wrong expected version
            var exception = await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
            {
                await StateRunner.ExecuteAtVersionAsync(
                    eventStore,
                    folder,
                    decider,
                    streamId,
                    new LatestVersionCommand(),
                    expectedVersion: 1);
            });

            Assert.Equal(1, exception.ExpectedVersion.Version);
            Assert.Equal(2, exception.ActualVersion.Version);
        }
    }

    [Fact]
    public async Task ExecuteAtVersionAsync_OnNewStream_WorksWithVersionZero()
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
            var streamId = new StreamIdentifier("MetadataVersionTest", "new-stream-test");

            // Act: Execute on new stream with expected version 0
            var result = await StateRunner.ExecuteAtVersionAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new LatestVersionCommand(),
                expectedVersion: 0);

            // Assert
            Assert.Single(result.Events);
            Assert.Equal(1, result.Version);
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
    public async Task ExecuteAtVersionAsync_IgnoresCommandExpectedVersionKey()
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
            var streamId = new StreamIdentifier("MetadataVersionTest", "ignore-attribute-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand(), registry);

            // Act: Use ExecuteAtVersionAsync with command that has ExpectedVersionKey
            // Should use explicit parameter, not look for metadata
            var result = await StateRunner.ExecuteAtVersionAsync(
                eventStore,
                folder,
                decider,
                streamId,
                new ExpectedVersionCommand("order-1"),
                expectedVersion: 1);

            // Assert: Success without providing metadata
            Assert.Single(result.Events);
            Assert.Equal(2, result.Version);
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

using Rickten.EventStore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Rickten.Aggregator.Tests;

/// <summary>
/// Tests for CommandVersionMode and expected version execution.
/// Covers CQRS stale-read protection scenarios.
/// </summary>
public class CommandVersionModeTests
{
    [Fact]
    public void CommandAttribute_DefaultVersionMode_IsLatestVersion()
    {
        // Arrange & Act
        var attribute = new CommandAttribute("TestAggregate");

        // Assert
        Assert.Equal(CommandVersionMode.LatestVersion, attribute.VersionMode);
    }

    [Fact]
    public void CommandAttribute_CanSetExpectedVersionMode()
    {
        // Arrange & Act
        var attribute = new CommandAttribute("TestAggregate")
        {
            VersionMode = CommandVersionMode.ExpectedVersion
        };

        // Assert
        Assert.Equal(CommandVersionMode.ExpectedVersion, attribute.VersionMode);
    }

    [Fact]
    public async Task ExecuteAsync_WithLatestVersionCommand_ExecutesAgainstCurrentState()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new VersionModeTestStateFolder(registry);
            var decider = new VersionModeTestDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "latest-version-test");

            // Act: Execute command (LatestVersion is default)
            var result = await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

            // Assert
            Assert.Single(result.Events);
            Assert.Equal(1, result.Version);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithExpectedVersionCommand_UsesCommandCarriedVersion()
    {
        // Arrange: Create stream with initial state
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new VersionModeTestStateFolder(registry);
            var decider = new VersionModeTestDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "expected-version-test");

            // Create initial version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

            // Act: Execute ExpectedVersion command at version 1
            var result = await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new ExpectedVersionCommand(1));

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
            var folder = new VersionModeTestStateFolder(registry);
            var decider = new VersionModeTestDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "version-mismatch-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

            // Advance to version 2
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

            // Act & Assert: Try to execute with stale expected version 1
            var exception = await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
            {
                await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new ExpectedVersionCommand(1));
            });

            Assert.Equal(1, exception.ExpectedVersion.Version);
            Assert.Equal(2, exception.ActualVersion.Version);
            Assert.Equal(streamId, exception.ExpectedVersion.Stream);
            Assert.Equal(streamId, exception.ActualVersion.Stream);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpectedVersionWithoutInterface_ThrowsInvalidOperationException()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new VersionModeTestStateFolder(registry);
            var decider = new VersionModeTestDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "missing-interface-test");

            // Act & Assert: Command has ExpectedVersion mode but doesn't implement interface
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new MissingInterfaceCommand());
            });

            Assert.Contains("VersionMode = ExpectedVersion", exception.Message);
            Assert.Contains("IExpectedVersionCommand", exception.Message);
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
            var folder = new VersionModeTestStateFolder(registry);
            var decider = new VersionModeTestDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "explicit-version-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

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
            var folder = new VersionModeTestStateFolder(registry);
            var decider = new VersionModeTestDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "explicit-mismatch-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

            // Advance to version 2
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

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
            var folder = new VersionModeTestStateFolder(registry);
            var decider = new VersionModeTestDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "new-stream-test");

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
            var folder = new VersionModeTestStateFolder(registry);
            var decider = new VersionModeTestDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "idempotent-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

            // Act: Execute idempotent command at expected version
            var result = await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new IdempotentExpectedVersionCommand(1));

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
            var folder = new VersionModeTestStateFolder(registry);
            var decider = new VersionModeTestDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "snapshot-validation-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

            // Save snapshot at version 1
            var snapshotPointer = new StreamPointer(streamId, 1);
            await snapshotStore.SaveSnapshotAsync(snapshotPointer, new VersionModeTestState(1));

            // Advance to version 2
            await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new LatestVersionCommand());

            // Act & Assert: Expected version check happens before snapshot loading
            var exception = await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
            {
                await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, new ExpectedVersionCommand(1), snapshotStore);
            });

            Assert.Equal(1, exception.ExpectedVersion.Version);
            Assert.Equal(2, exception.ActualVersion.Version);
        }
    }

    [Fact]
    public async Task ExecuteAtVersionAsync_DoesNotRunDeciderOnVersionMismatch()
    {
        // Arrange
        var (connection, serviceProvider) = TestServiceFactory.CreateServiceProvider();
        using (connection)
        using (var scope = serviceProvider.CreateScope())
        {
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var registry = scope.ServiceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
            var folder = new VersionModeTestStateFolder(registry);
            var trackingDecider = new TrackingDecider();
            var streamId = new StreamIdentifier("VersionModeTest", "decider-not-run-test");

            // Create version 1
            await StateRunner.ExecuteAsync(eventStore, folder, trackingDecider, streamId, new LatestVersionCommand());

            // Reset tracking
            trackingDecider.ExecutedCommands.Clear();

            // Act & Assert: Version mismatch should fail before decider runs
            await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
            {
                await StateRunner.ExecuteAtVersionAsync(
                    eventStore,
                    folder,
                    trackingDecider,
                    streamId,
                    new LatestVersionCommand(),
                    expectedVersion: 0); // Wrong version
            });

            // Assert: Decider was never called
            Assert.Empty(trackingDecider.ExecutedCommands);
        }
    }
}

// Test domain for version mode tests
[Aggregate("VersionModeTest")]
public record VersionModeTestState(int Count = 0);

[Event("VersionModeTest", "Event", 1)]
public record VersionModeTestEvent;

[Command("VersionModeTest", VersionMode = CommandVersionMode.LatestVersion)]
public record LatestVersionCommand;

[Command("VersionModeTest", VersionMode = CommandVersionMode.ExpectedVersion)]
public record ExpectedVersionCommand(long ExpectedVersion) : IExpectedVersionCommand;

[Command("VersionModeTest", VersionMode = CommandVersionMode.ExpectedVersion)]
public record IdempotentExpectedVersionCommand(long ExpectedVersion) : IExpectedVersionCommand;

[Command("VersionModeTest", VersionMode = CommandVersionMode.ExpectedVersion)]
public record MissingInterfaceCommand; // Missing IExpectedVersionCommand implementation

public class VersionModeTestStateFolder : StateFolder<VersionModeTestState>
{
    public VersionModeTestStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override VersionModeTestState InitialState() => new(0);

    protected VersionModeTestState When(VersionModeTestEvent e, VersionModeTestState state)
    {
        return state with { Count = state.Count + 1 };
    }
}

public class VersionModeTestDecider : CommandDecider<VersionModeTestState, object>
{
    protected override IReadOnlyList<object> ExecuteCommand(VersionModeTestState state, object command)
    {
        return command switch
        {
            LatestVersionCommand => Event(new VersionModeTestEvent()),
            ExpectedVersionCommand => Event(new VersionModeTestEvent()),
            IdempotentExpectedVersionCommand => NoEvents(),
            MissingInterfaceCommand => Event(new VersionModeTestEvent()),
            _ => throw new InvalidOperationException($"Unknown command type: {command.GetType().Name}")
        };
    }
}

public class TrackingDecider : CommandDecider<VersionModeTestState, object>
{
    public List<object> ExecutedCommands { get; } = new();

    protected override IReadOnlyList<object> ExecuteCommand(VersionModeTestState state, object command)
    {
        ExecutedCommands.Add(command);
        return Event(new VersionModeTestEvent());
    }
}

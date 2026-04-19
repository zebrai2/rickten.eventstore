using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rickten.Aggregator;
using Rickten.EventStore;
using Rickten.Projector;

namespace Rickten.Reactor.Tests;

/// <summary>
/// Test events for reaction testing
/// </summary>
[Event("MembershipDefinition", "Changed", 1)]
public record MembershipDefinitionChangedEvent(string DefinitionId, string Name);

[Event("User", "Registered", 1)]
public record UserRegisteredEvent(string UserId, string Email, string MembershipDefinitionId);

/// <summary>
/// Test command
/// </summary>
[Command("Membership")]
public record RecalculateMembershipCommand(string MembershipId, string Reason);

/// <summary>
/// Test event for membership recalculation
/// </summary>
[Event("Membership", "Recalculated", 1)]
public record MembershipRecalculatedEvent(string MembershipId, string Reason);

/// <summary>
/// Test aggregate state
/// </summary>
[Aggregate("Membership")]
public record MembershipState(string MembershipId, int CalculationCount);

/// <summary>
/// Test state folder
/// </summary>
public class MembershipStateFolder : StateFolder<MembershipState>
{
    public MembershipStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override MembershipState InitialState() => new MembershipState("", 0);

    protected MembershipState When(MembershipRecalculatedEvent e, MembershipState state)
    {
        return state with { MembershipId = e.MembershipId, CalculationCount = state.CalculationCount + 1 };
    }
}

/// <summary>
/// Test command decider
/// </summary>
public class MembershipCommandDecider : CommandDecider<MembershipState, RecalculateMembershipCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(MembershipState state, RecalculateMembershipCommand command)
    {
        // For testing, produce a recalculated event
        return Event(new MembershipRecalculatedEvent(command.MembershipId, command.Reason));
    }
}

/// <summary>
/// Projection view that maps definition IDs to affected membership stream identifiers
/// In a real system, this would query a read model to find memberships using a definition
/// </summary>
public record MembershipDefinitionView(Dictionary<string, List<string>> DefinitionToMemberships);

/// <summary>
/// Projection that builds a map of definition -> membership relationships
/// </summary>
[Rickten.Projector.Projection("MembershipDefinitionIndex")]
public class MembershipDefinitionProjection : Rickten.Projector.Projection<MembershipDefinitionView>
{
    public override MembershipDefinitionView InitialView() => 
        new MembershipDefinitionView(new Dictionary<string, List<string>>());

    protected override MembershipDefinitionView ApplyEvent(MembershipDefinitionView view, StreamEvent streamEvent)
    {
        return streamEvent.Event switch
        {
            MembershipDefinitionChangedEvent evt => AddDefinition(view, evt.DefinitionId),
            UserRegisteredEvent evt => AddMembershipToDefinition(view, evt.MembershipDefinitionId, evt.UserId),
            _ => view
        };
    }

    private MembershipDefinitionView AddDefinition(MembershipDefinitionView view, string definitionId)
    {
        var map = new Dictionary<string, List<string>>(view.DefinitionToMemberships);
        if (!map.ContainsKey(definitionId))
        {
            map[definitionId] = new List<string>();
        }
        return view with { DefinitionToMemberships = map };
    }

    private MembershipDefinitionView AddMembershipToDefinition(MembershipDefinitionView view, string definitionId, string membershipId)
    {
        var map = new Dictionary<string, List<string>>(view.DefinitionToMemberships);
        if (!map.ContainsKey(definitionId))
        {
            map[definitionId] = new List<string>();
        }
        if (!map[definitionId].Contains(membershipId))
        {
            map[definitionId].Add(membershipId);
        }
        return view with { DefinitionToMemberships = map };
    }
}

/// <summary>
/// Example reaction from the spec - now with projection-based stream selection
/// </summary>
[Reaction("MembershipDefinitionChanged", EventTypes = new[] { "MembershipDefinition.Changed.v1" })]
public sealed class MembershipDefinitionChangedReaction : Reaction<MembershipDefinitionView, RecalculateMembershipCommand>
{
    private readonly MembershipDefinitionProjection _projection = new();

    public MembershipDefinitionChangedReaction(EventStore.TypeMetadata.ITypeMetadataRegistry registry) 
        : base(registry) { }

    public override IProjection<MembershipDefinitionView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(MembershipDefinitionView view, StreamEvent trigger)
    {
        // Extract the definition ID from the trigger event
        var evt = (MembershipDefinitionChangedEvent)trigger.Event;

        // Look up all memberships using this definition in the projection view
        if (view.DefinitionToMemberships.TryGetValue(evt.DefinitionId, out var membershipIds))
        {
            foreach (var membershipId in membershipIds)
            {
                yield return new StreamIdentifier("Membership", membershipId);
            }
        }
    }

    protected override RecalculateMembershipCommand BuildCommand(StreamIdentifier stream, MembershipDefinitionView view, StreamEvent trigger)
    {
        var evt = (MembershipDefinitionChangedEvent)trigger.Event;
        return new RecalculateMembershipCommand(
            stream.Identifier,
            $"Definition changed: {evt.Name}");
    }
}

/// <summary>
/// Another example reaction for user registration - single stream case
/// </summary>
[Reaction("UserRegisteredReaction", EventTypes = new[] { "User.Registered.v1" })]
public sealed class UserRegisteredReaction : Reaction<MembershipDefinitionView, RecalculateMembershipCommand>
{
    private readonly MembershipDefinitionProjection _projection = new();

    public UserRegisteredReaction(EventStore.TypeMetadata.ITypeMetadataRegistry registry) 
        : base(registry) { }

    public override IProjection<MembershipDefinitionView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(MembershipDefinitionView view, StreamEvent trigger)
    {
        // Single stream: the user's membership
        var evt = (UserRegisteredEvent)trigger.Event;
        yield return new StreamIdentifier("Membership", evt.UserId);
    }

    protected override RecalculateMembershipCommand BuildCommand(StreamIdentifier stream, MembershipDefinitionView view, StreamEvent trigger)
    {
        var evt = (UserRegisteredEvent)trigger.Event;
        return new RecalculateMembershipCommand(
            stream.Identifier,
            $"User registered: {evt.Email}");
    }
}

/// <summary>
/// In-memory implementation of IProjectionStore for testing
/// </summary>
public class InMemoryProjectionStore : IProjectionStore
{
    private readonly Dictionary<(string Namespace, string Key), object> _projections = new();

    public Task<EventStore.Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        CancellationToken cancellationToken = default)
    {
        return LoadProjectionAsync<TState>(projectionKey, "system", cancellationToken);
    }

    public Task<EventStore.Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        var key = (@namespace, projectionKey);
        if (_projections.TryGetValue(key, out var stored) && stored is EventStore.Projection<TState> projection)
        {
            return Task.FromResult<EventStore.Projection<TState>?>(projection);
        }
        return Task.FromResult<EventStore.Projection<TState>?>(null);
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
        _projections[key] = new EventStore.Projection<TState>(state, globalPosition);
        return Task.CompletedTask;
    }

    public long? GetCheckpoint(string reactionKey, string @namespace = "reaction")
    {
        var key = (@namespace, $"{reactionKey}:trigger");
        if (_projections.TryGetValue(key, out var stored) && stored is EventStore.Projection<long> checkpoint)
        {
            return checkpoint.State;
        }
        return null;
    }
}

public class ReactionRunnerTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly IServiceProvider _serviceProvider;
    private readonly InMemoryProjectionStore _projectionStore;

    public ReactionRunnerTests()
    {
        (_connection, _serviceProvider) = TestServiceFactory.CreateServiceProvider();
        _projectionStore = new InMemoryProjectionStore();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task CatchUpAsync_ProcessesMatchingEvents_ExecutesCommandsAgainstMultipleStreams()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var registry = _serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
        var reaction = new MembershipDefinitionChangedReaction(registry);
        var folder = new MembershipStateFolder(registry);
        var decider = new MembershipCommandDecider();

        // Create a definition
        var defStream = new StreamIdentifier("MembershipDefinition", "def-1");
        await eventStore.AppendAsync(new StreamPointer(defStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-1", "Premium"), null)
        });

        // Register two users with this definition (creates memberships)
        var user1Stream = new StreamIdentifier("User", "user-1");
        var user2Stream = new StreamIdentifier("User", "user-2");
        await eventStore.AppendAsync(new StreamPointer(user1Stream, 0), new List<AppendEvent>
        {
            new AppendEvent(new UserRegisteredEvent("user-1", "user1@test.com", "def-1"), null)
        });
        await eventStore.AppendAsync(new StreamPointer(user2Stream, 0), new List<AppendEvent>
        {
            new AppendEvent(new UserRegisteredEvent("user-2", "user2@test.com", "def-1"), null)
        });

        // Change the definition again - this should trigger commands to both membership streams
        await eventStore.AppendAsync(new StreamPointer(defStream, 1), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-1", "Premium Plus"), null)
        });

        // Act
        var AggregateRepository = new AggregateRepository<MembershipState>(eventStore, folder, NoOpSnapshotStore.Instance);
        var executor = new AggregateCommandExecutor<MembershipState, RecalculateMembershipCommand>(AggregateRepository, decider, registry);
        var lastPosition = await ReactionRunner.CatchUpAsync(
            eventStore,
            _projectionStore,
            reaction,
            executor);

        // Assert
        Assert.True(lastPosition > 0);

        // Verify checkpoint was saved
        var checkpoint = _projectionStore.GetCheckpoint("MembershipDefinitionChanged");
        Assert.Equal(lastPosition, checkpoint);

        // Verify commands were executed against both membership streams
        var membership1Stream = new StreamIdentifier("Membership", "user-1");
        var membership1Events = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(membership1Stream, CancellationToken.None))
        {
            membership1Events.Add(evt);
        }

        var membership2Stream = new StreamIdentifier("Membership", "user-2");
        var membership2Events = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(membership2Stream, CancellationToken.None))
        {
            membership2Events.Add(evt);
        }

        // Both memberships should have received a recalculate command
        Assert.Single(membership1Events);
        Assert.Single(membership2Events);
    }

    [Fact]
    public async Task CatchUpAsync_ResumeFromCheckpoint_OnlyProcessesNewEvents()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var registry = _serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
        var reaction = new MembershipDefinitionChangedReaction(registry);
        var folder = new MembershipStateFolder(registry);
        var decider = new MembershipCommandDecider();

        // Create initial definition and user
        var defStream = new StreamIdentifier("MembershipDefinition", "def-2");
        await eventStore.AppendAsync(new StreamPointer(defStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-2", "Basic"), null)
        });

        var userStream = new StreamIdentifier("User", "user-3");
        await eventStore.AppendAsync(new StreamPointer(userStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new UserRegisteredEvent("user-3", "user3@test.com", "def-2"), null)
        });

        // First definition change
        await eventStore.AppendAsync(new StreamPointer(defStream, 1), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-2", "Standard"), null)
        });

        // First catch-up
        var AggregateRepository = new AggregateRepository<MembershipState>(eventStore, folder, NoOpSnapshotStore.Instance);
        var executor = new AggregateCommandExecutor<MembershipState, RecalculateMembershipCommand>(AggregateRepository, decider, registry);
        var firstPosition = await ReactionRunner.CatchUpAsync(
            eventStore,
            _projectionStore,
            reaction,
            executor);

        // Add another definition change
        await eventStore.AppendAsync(new StreamPointer(defStream, 2), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-2", "Premium"), null)
        });

        // Second catch-up
        var secondPosition = await ReactionRunner.CatchUpAsync(
            eventStore,
            _projectionStore,
            reaction,
            executor);

        // Assert
        Assert.True(secondPosition > firstPosition);

        // Verify only 2 commands total (one for each definition change that matched)
        var membershipStream = new StreamIdentifier("Membership", "user-3");
        var events = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(membershipStream, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task CatchUpAsync_WithEventTypeFilter_OnlyProcessesMatchingEventTypes()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var registry = _serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
        var reaction = new MembershipDefinitionChangedReaction(registry);
        var folder = new MembershipStateFolder(registry);
        var decider = new MembershipCommandDecider();

        // Create definition
        var defStream = new StreamIdentifier("MembershipDefinition", "def-3");
        await eventStore.AppendAsync(new StreamPointer(defStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-3", "Gold"), null)
        });

        // Register user (this builds the projection mapping)
        var userStream = new StreamIdentifier("User", "user-4");
        await eventStore.AppendAsync(new StreamPointer(userStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new UserRegisteredEvent("user-4", "user4@test.com", "def-3"), null)
        });

        // Another definition change - this should now find the user and trigger a command
        await eventStore.AppendAsync(new StreamPointer(defStream, 1), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-3", "Platinum"), null)
        });

        // Act - MembershipDefinitionChangedReaction only triggers on MembershipDefinition.Changed events
        var AggregateRepository = new AggregateRepository<MembershipState>(eventStore, folder, NoOpSnapshotStore.Instance);
        var executor = new AggregateCommandExecutor<MembershipState, RecalculateMembershipCommand>(AggregateRepository, decider, registry);
        var lastPosition = await ReactionRunner.CatchUpAsync(
            eventStore,
            _projectionStore,
            reaction,
            executor);

        // Assert - Two definition changes occurred, but only the second one had a registered user
        // First definition change: no memberships yet (0 commands)
        // Second definition change: 1 membership found (1 command)
        // Total: 1 command executed
        var membershipStream = new StreamIdentifier("Membership", "user-4");
        var events = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(membershipStream, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Single(events);
    }

    [Fact]
    public async Task CatchUpAsync_MultipleReactions_MaintainSeparateCheckpoints()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var registry = _serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
        var reaction1 = new MembershipDefinitionChangedReaction(registry);
        var reaction2 = new UserRegisteredReaction(registry);
        var folder = new MembershipStateFolder(registry);
        var decider = new MembershipCommandDecider();

        // Create events
        var defStream = new StreamIdentifier("MembershipDefinition", "def-4");
        await eventStore.AppendAsync(new StreamPointer(defStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-4", "Platinum"), null)
        });

        var userStream = new StreamIdentifier("User", "user-2");
        await eventStore.AppendAsync(new StreamPointer(userStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new UserRegisteredEvent("user-2", "user@test.com", "def-4"), null)
        });

        // Act - run both reactions
        var AggregateRepository = new AggregateRepository<MembershipState>(eventStore, folder, NoOpSnapshotStore.Instance);
        var executor = new AggregateCommandExecutor<MembershipState, RecalculateMembershipCommand>(AggregateRepository, decider, registry);
        var pos1 = await ReactionRunner.CatchUpAsync(
            eventStore,
            _projectionStore,
            reaction1,
            executor);

        var pos2 = await ReactionRunner.CatchUpAsync(
            eventStore,
            _projectionStore,
            reaction2,
            executor);

        // Assert - both should have processed their respective events
        var checkpoint1 = _projectionStore.GetCheckpoint("MembershipDefinitionChanged");
        var checkpoint2 = _projectionStore.GetCheckpoint("UserRegisteredReaction");

        Assert.True(checkpoint1 > 0);
        Assert.True(checkpoint2 > 0);
        // Checkpoints may differ based on event ordering
    }

    [Fact]
    public async Task CatchUpAsync_WithoutReactionName_UsesClassName()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var registry = _serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();

        // Create a reaction without the attribute - should throw because it's not registered
        var exception = Assert.Throws<InvalidOperationException>(() => 
            new TestReactionWithoutAttribute(registry));

        // Assert
        Assert.Contains("not registered in the TypeMetadataRegistry", exception.Message);
    }

    [Fact]
    public async Task CatchUpAsync_WhenProjectionAheadOfReaction_RebuildsProjectionCorrectly()
    {
        // This test verifies the fix for the projection-ahead-of-reaction bug.
        // Scenario: Projection was caught up to position 100, but reaction failed at position 50.
        // When we restart, we need to rebuild the projection to position 50 so that
        // triggers 51-100 are processed with the correct historical projection state.

        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var registry = _serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
        var reaction = new MembershipDefinitionChangedReaction(registry);
        var folder = new MembershipStateFolder(registry);
        var decider = new MembershipCommandDecider();

        // Create definition and user
        var defStream = new StreamIdentifier("MembershipDefinition", "def-rebuild");
        await eventStore.AppendAsync(new StreamPointer(defStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-rebuild", "v1"), null)
        });

        var userStream = new StreamIdentifier("User", "user-rebuild");
        await eventStore.AppendAsync(new StreamPointer(userStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new UserRegisteredEvent("user-rebuild", "user@test.com", "def-rebuild"), null)
        });

        // Create more definition changes
        await eventStore.AppendAsync(new StreamPointer(defStream, 1), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-rebuild", "v2"), null),
            new AppendEvent(new MembershipDefinitionChangedEvent("def-rebuild", "v3"), null)
        });

        // Simulate scenario: projection caught up to end, but reaction only processed first trigger
        // Manually set projection ahead of reaction
        var allEventsPosition = 4L; // After all 4 events
        var firstTriggerPosition = 1L; // After first definition change

        // Build full projection state (as if it was caught up)
        var fullView = new MembershipDefinitionView(new Dictionary<string, List<string>>());
        fullView = new MembershipDefinitionProjection().Apply(fullView, 
            new StreamEvent(new StreamPointer(defStream, 1), 1, 
                new MembershipDefinitionChangedEvent("def-rebuild", "v1"), Array.Empty<EventMetadata>()));
        fullView = new MembershipDefinitionProjection().Apply(fullView,
            new StreamEvent(new StreamPointer(userStream, 1), 2,
                new UserRegisteredEvent("user-rebuild", "user@test.com", "def-rebuild"), Array.Empty<EventMetadata>()));

        // Save checkpoints: projection ahead, reaction behind
        await _projectionStore.SaveProjectionAsync("MembershipDefinitionChanged:trigger", firstTriggerPosition, firstTriggerPosition, "reaction");
        await _projectionStore.SaveProjectionAsync("MembershipDefinitionChanged:projection", allEventsPosition, fullView, "reaction");

        // Act - catch up should rebuild projection to reaction position (1), then process triggers 2-3
        var AggregateRepository = new AggregateRepository<MembershipState>(eventStore, folder, NoOpSnapshotStore.Instance);
        var executor = new AggregateCommandExecutor<MembershipState, RecalculateMembershipCommand>(AggregateRepository, decider, registry);
        var finalPosition = await ReactionRunner.CatchUpAsync(
            eventStore,
            _projectionStore,
            reaction,
            executor);

        // Assert
        Assert.True(finalPosition > firstTriggerPosition);

        // Verify commands were executed for triggers 2 and 3
        var membershipStream = new StreamIdentifier("Membership", "user-rebuild");
        var events = new List<StreamEvent>();
        await foreach (var evt in eventStore.LoadAsync(membershipStream, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Should have 2 recalculations (for triggers at positions 3 and 4)
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task CatchUpAsync_WhenProjectionAheadOfReaction_LogsWarning()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        var registry = _serviceProvider.GetRequiredService<EventStore.TypeMetadata.ITypeMetadataRegistry>();
        var reaction = new MembershipDefinitionChangedReaction(registry);
        var folder = new MembershipStateFolder(registry);
        var decider = new MembershipCommandDecider();

        // Create a simple logger that captures log messages
        var logMessages = new List<string>();
        var loggerFactory = LoggerFactory.Create(builder => 
        {
            builder.AddProvider(new TestLoggerProvider(logMessages));
        });
        var logger = loggerFactory.CreateLogger("Test");

        // Create definition
        var defStream = new StreamIdentifier("MembershipDefinition", "def-log");
        await eventStore.AppendAsync(new StreamPointer(defStream, 0), new List<AppendEvent>
        {
            new AppendEvent(new MembershipDefinitionChangedEvent("def-log", "v1"), null)
        });

        // Simulate projection ahead scenario
        await _projectionStore.SaveProjectionAsync("MembershipDefinitionChanged:trigger", 0, 0L, "reaction");
        await _projectionStore.SaveProjectionAsync("MembershipDefinitionChanged:projection", 10, 
            new MembershipDefinitionView(new Dictionary<string, List<string>>()), "reaction");

        // Act
        var AggregateRepository = new AggregateRepository<MembershipState>(eventStore, folder, NoOpSnapshotStore.Instance);
        var executor = new AggregateCommandExecutor<MembershipState, RecalculateMembershipCommand>(AggregateRepository, decider, registry);
        await ReactionRunner.CatchUpAsync(
            eventStore,
            _projectionStore,
            reaction,
            executor,
            logger: logger);

        // Assert - verify warning was logged
        Assert.Contains(logMessages, msg => 
            msg.Contains("projection is ahead of reaction checkpoint") &&
            msg.Contains("Rebuilding projection"));
    }
}

// Simple test logger for capturing log messages
public class TestLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages;

    public TestLoggerProvider(List<string> messages)
    {
        _messages = messages;
    }

    public ILogger CreateLogger(string categoryName) => new TestLogger(_messages);
    public void Dispose() { }
}

public class TestLogger : ILogger
{
    private readonly List<string> _messages;

    public TestLogger(List<string> messages)
    {
        _messages = messages;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _messages.Add(formatter(state, exception));
    }
}

/// <summary>
/// Test reaction without attribute to verify error handling
/// </summary>
public class TestReactionWithoutAttribute : Reaction<MembershipDefinitionView, RecalculateMembershipCommand>
{
    private readonly MembershipDefinitionProjection _projection = new();

    public TestReactionWithoutAttribute(EventStore.TypeMetadata.ITypeMetadataRegistry registry) 
        : base(registry) { }

    public override IProjection<MembershipDefinitionView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(MembershipDefinitionView view, StreamEvent trigger)
    {
        yield return new StreamIdentifier("Test", "test");
    }

    protected override RecalculateMembershipCommand BuildCommand(StreamIdentifier stream, MembershipDefinitionView view, StreamEvent trigger)
    {
        return new RecalculateMembershipCommand("test", "test");
    }
}

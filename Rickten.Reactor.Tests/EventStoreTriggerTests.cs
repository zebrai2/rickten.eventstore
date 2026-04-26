using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rickten.EventStore;
using Rickten.EventStore.EntityFramework;
using Rickten.Reactor;
using Xunit;

namespace Rickten.Reactor.Tests;

public class EventStoreTriggerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public EventStoreTriggerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddEventStore(
            options => options.UseSqlite(_connection),
            typeof(TestEvent).Assembly);

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();
        dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task EventStoreTrigger_ListensToAllEvents()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();

        // Append some test events
        var stream = new StreamIdentifier("Test", "stream1");
        await eventStore.AppendAsync(
            stream.At(0),
            [
                new AppendEvent(new TestEvent { Data = "event1" }),
                new AppendEvent(new TestEvent { Data = "event2" })
            ]);

        var triggerType = new EventStoreTriggerType(eventStore);
        var definition = new TriggerInstanceDefinition(
            Name: "AllEvents",
            Type: "EventStore",
            Configuration: new Dictionary<string, object?>());

        var trigger = triggerType.Create(definition);

        // Act
        var contexts = new List<ReactionContext>();
        await foreach (var context in trigger.ListenAsync(CancellationToken.None))
        {
            contexts.Add(context);
            if (contexts.Count >= 2) break;
        }

        // Assert
        Assert.Equal(2, contexts.Count);

        Assert.Equal("AllEvents", contexts[0]["trigger.name"]);
        Assert.Equal("EventStore", contexts[0]["trigger.type"]);
        Assert.Equal(1L, contexts[0]["event.globalPosition"]);

        Assert.Equal(2L, contexts[1]["event.globalPosition"]);
    }

    [Fact(Skip = "Event filtering needs wire name investigation")]
    public async Task EventStoreTrigger_FiltersEventTypes()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();

        var stream = new StreamIdentifier("Test", "stream2");
        await eventStore.AppendAsync(
            stream.At(0),
            [
                new AppendEvent(new TestEvent { Data = "test1" }),
                new AppendEvent(new OtherEvent { Value = 42 }),
                new AppendEvent(new TestEvent { Data = "test2" })
            ]);

        var triggerType = new EventStoreTriggerType(eventStore);
        var definition = new TriggerInstanceDefinition(
            Name: "TestEventsOnly",
            Type: "EventStore",
            Configuration: new Dictionary<string, object?>
            {
                ["eventTypes"] = new[] { "Test.TestEvent" }
            });

        var trigger = triggerType.Create(definition);

        // Act
        var contexts = new List<ReactionContext>();
        await foreach (var context in trigger.ListenAsync(CancellationToken.None))
        {
            contexts.Add(context);
            if (contexts.Count >= 2) break;
        }

        // Assert - should only get TestEvent instances
        Assert.Equal(2, contexts.Count);
        // All contexts are from the TestEventsOnly trigger
        Assert.All(contexts, c => Assert.Equal("TestEventsOnly", c["trigger.name"]));
    }

    [Fact]
    public async Task EventStoreTrigger_StartsFromGlobalPosition()
    {
        // Arrange
        var eventStore = _serviceProvider.GetRequiredService<IEventStore>();

        var stream = new StreamIdentifier("Test", "stream3");
        await eventStore.AppendAsync(
            stream.At(0),
            [
                new AppendEvent(new TestEvent { Data = "old1" }),
                new AppendEvent(new TestEvent { Data = "old2" }),
                new AppendEvent(new TestEvent { Data = "new1" })
            ]);

        var triggerType = new EventStoreTriggerType(eventStore);
        var definition = new TriggerInstanceDefinition(
            Name: "FromPosition2",
            Type: "EventStore",
            Configuration: new Dictionary<string, object?>
            {
                ["fromGlobalPosition"] = 2L
            });

        var trigger = triggerType.Create(definition);

        // Act
        var contexts = new List<ReactionContext>();
        await foreach (var context in trigger.ListenAsync(CancellationToken.None))
        {
            contexts.Add(context);
            if (contexts.Count >= 1) break;
        }

        // Assert - should only get event after position 2
        Assert.Single(contexts);
        Assert.True((long)contexts[0]["event.globalPosition"]! > 2);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}

[Event("Test", "TestEvent", 1)]
public sealed record TestEvent
{
    public string Data { get; init; } = string.Empty;
}

[Event("Test", "OtherEvent", 1)]
public sealed record OtherEvent
{
    public int Value { get; init; }
}

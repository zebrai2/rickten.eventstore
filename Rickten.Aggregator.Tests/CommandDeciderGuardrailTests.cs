using Rickten.EventStore;
using Xunit;

namespace Rickten.Aggregator.Tests;

/// <summary>
/// Tests for CommandDecider guardrails and validation.
/// Covers aggregate mismatch detection, null handling, and event validation.
/// </summary>
public class CommandDeciderGuardrailTests
{
    private static readonly EventStore.TypeMetadata.ITypeMetadataRegistry Registry = TestTypeMetadataRegistry.Create();

    [Fact]
    public void CommandDecider_WithNullCommand_ThrowsArgumentNullException()
    {
        // Arrange
        var decider = new GuardrailTestDecider();
        var state = new GuardrailTestState();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            decider.Execute(state, null!);
        });
    }

    [Fact]
    public void CommandDecider_WithCommandFromDifferentAggregate_ThrowsInvalidOperationException()
    {
        // Arrange: Decider for "GuardrailTest", command for "OtherAggregate"
        var decider = new GuardrailTestDecider();
        var state = new GuardrailTestState();
        var wrongCommand = new OtherAggregateCommand();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            decider.Execute(state, wrongCommand);
        });

        // Verify error message quality
        Assert.Contains("Command 'OtherAggregateCommand' belongs to aggregate 'OtherAggregate'", exception.Message);
        Assert.Contains("but this CommandDecider is for aggregate 'GuardrailTest'", exception.Message);
        Assert.Contains("Commands must match their aggregate's context", exception.Message);
    }

    [Fact]
    public void CommandDecider_WithProducedEventFromDifferentAggregate_ThrowsInvalidOperationException()
    {
        // Arrange: Decider that produces events from wrong aggregate
        var decider = new ProducesWrongAggregateEventsDecider();
        var state = new GuardrailTestState();
        var command = new GuardrailTestCommand();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            decider.Execute(state, command);
        });

        // Verify error message quality
        Assert.Contains("Event 'OtherAggregateEvent' belongs to aggregate 'OtherAggregate'", exception.Message);
        Assert.Contains("but this CommandDecider is for aggregate 'GuardrailTest'", exception.Message);
        Assert.Contains("Events must match their aggregate's context", exception.Message);
    }

    [Fact]
    public void CommandDecider_WithValidCommand_ExecutesSuccessfully()
    {
        // Arrange
        var decider = new GuardrailTestDecider();
        var state = new GuardrailTestState();
        var command = new GuardrailTestCommand();

        // Act
        var events = decider.Execute(state, command);

        // Assert
        Assert.Single(events);
        Assert.IsType<GuardrailTestEvent>(events[0]);
    }

    [Fact]
    public void CommandDecider_WithCommandWithoutAttribute_DoesNotThrow()
    {
        // Arrange: Command without [Command] attribute should be allowed
        // (attribute is optional, only validated if present)
        var decider = new UnattributedCommandDecider();
        var state = new GuardrailTestState();
        var command = new UnattributedCommand();

        // Act
        var events = decider.Execute(state, command);

        // Assert
        Assert.Single(events);
    }

    [Fact]
    public void CommandDecider_WithEventWithoutAttribute_DoesNotThrow()
    {
        // Arrange: Event without [Event] attribute should be allowed
        // (attribute is optional, only validated if present)
        var decider = new UnattributedEventDecider();
        var state = new GuardrailTestState();
        var command = new GuardrailTestCommand();

        // Act
        var events = decider.Execute(state, command);

        // Assert
        Assert.Single(events);
    }

    [Fact]
    public void CommandDecider_StateWithoutAggregateAttribute_ThrowsOnConstruction()
    {
        // Arrange & Act & Assert: Constructor should validate state has [Aggregate]
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            new StateWithoutAggregateDecider();
        });

        // Verify error message
        Assert.Contains("State type 'StateWithoutAggregate' must be decorated with [Aggregate] attribute", exception.Message);
        Assert.Contains("Add [Aggregate(\"YourAggregateName\")] to your state record/class", exception.Message);
    }

    [Fact]
    public void CommandDecider_AggregateName_ReturnsCorrectName()
    {
        // Arrange
        var decider = new GuardrailTestDecider();

        // Act
        var aggregateName = decider.GetAggregateName(); // We'll add a public accessor for testing

        // Assert
        Assert.Equal("GuardrailTest", aggregateName);
    }

    [Fact]
    public void CommandDecider_CreateStreamId_CreatesCorrectIdentifier()
    {
        // Arrange
        var decider = new GuardrailTestDecider();

        // Act
        var streamId = decider.CreatePublicStreamId("123");

        // Assert
        Assert.Equal("GuardrailTest", streamId.StreamType);
        Assert.Equal("123", streamId.Identifier);
    }

    [Fact]
    public void CommandDecider_RequireHelper_ThrowsWhenConditionFalse()
    {
        // Arrange
        var decider = new GuardrailTestDecider();
        var state = new GuardrailTestState();
        var command = new RequireTestCommand { ShouldFail = true };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            decider.Execute(state, command);
        });

        Assert.Equal("Condition not met", exception.Message);
    }

    [Fact]
    public void CommandDecider_RequireHelper_DoesNotThrowWhenConditionTrue()
    {
        // Arrange
        var decider = new GuardrailTestDecider();
        var state = new GuardrailTestState();
        var command = new RequireTestCommand { ShouldFail = false };

        // Act
        var events = decider.Execute(state, command);

        // Assert
        Assert.Single(events);
    }

    [Fact]
    public void CommandDecider_NoEventsHelper_ReturnsEmptyList()
    {
        // Arrange
        var decider = new GuardrailTestDecider();
        var state = new GuardrailTestState();
        var command = new NoEventsCommand();

        // Act
        var events = decider.Execute(state, command);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void CommandDecider_MultipleEventsHelper_ReturnsAllEvents()
    {
        // Arrange
        var decider = new GuardrailTestDecider();
        var state = new GuardrailTestState();
        var command = new MultipleEventsCommand();

        // Act
        var events = decider.Execute(state, command);

        // Assert
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.IsType<GuardrailTestEvent>(e));
    }
}

// Test domain for guardrail tests
[Aggregate("GuardrailTest")]
public record GuardrailTestState;

[Command("GuardrailTest")]
public record GuardrailTestCommand;

[Command("GuardrailTest")]
public record RequireTestCommand
{
    public bool ShouldFail { get; init; }
}

[Command("GuardrailTest")]
public record NoEventsCommand;

[Command("GuardrailTest")]
public record MultipleEventsCommand;

[Event("GuardrailTest", "Event", 1)]
public record GuardrailTestEvent;

// Different aggregate for mismatch testing
[Aggregate("OtherAggregate")]
public record OtherAggregateState;

[Command("OtherAggregate")]
public record OtherAggregateCommand;

[Event("OtherAggregate", "Event", 1)]
public record OtherAggregateEvent;

// Unattributed types for testing optional attribute validation
public record UnattributedCommand;
public record UnattributedEvent;

// State without [Aggregate] attribute
public record StateWithoutAggregate;

// Deciders
public class GuardrailTestDecider : CommandDecider<GuardrailTestState, object>
{
    protected override IReadOnlyList<object> ExecuteCommand(GuardrailTestState state, object command)
    {
        return command switch
        {
            GuardrailTestCommand => Event(new GuardrailTestEvent()),
            RequireTestCommand rtc => ExecuteRequireTest(rtc),
            NoEventsCommand => NoEvents(),
            MultipleEventsCommand => Events(new GuardrailTestEvent(), new GuardrailTestEvent(), new GuardrailTestEvent()),
            _ => NoEvents()
        };
    }

    private IReadOnlyList<object> ExecuteRequireTest(RequireTestCommand command)
    {
        Require(!command.ShouldFail, "Condition not met");
        return Event(new GuardrailTestEvent());
    }

    // Public accessors for testing protected members
    public string GetAggregateName() => AggregateName;
    public StreamIdentifier CreatePublicStreamId(string id) => CreateStreamId(id);
}

public class ProducesWrongAggregateEventsDecider : CommandDecider<GuardrailTestState, GuardrailTestCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(GuardrailTestState state, GuardrailTestCommand command)
    {
        // This should be caught by event validation
        return Event(new OtherAggregateEvent());
    }
}

public class UnattributedCommandDecider : CommandDecider<GuardrailTestState, UnattributedCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(GuardrailTestState state, UnattributedCommand command)
    {
        return Event(new GuardrailTestEvent());
    }
}

public class UnattributedEventDecider : CommandDecider<GuardrailTestState, GuardrailTestCommand>
{
    protected override IReadOnlyList<object> ExecuteCommand(GuardrailTestState state, GuardrailTestCommand command)
    {
        return Event(new UnattributedEvent());
    }
}

public class StateWithoutAggregateDecider : CommandDecider<StateWithoutAggregate, object>
{
    protected override IReadOnlyList<object> ExecuteCommand(StateWithoutAggregate state, object command)
    {
        return NoEvents();
    }
}

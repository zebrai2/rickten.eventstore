using Rickten.EventStore;
using Xunit;

namespace Rickten.Aggregator.Tests;

public class ValidationTests
{
    private static readonly EventStore.TypeMetadata.ITypeMetadataRegistry Registry = TestTypeMetadataRegistry.Create();

    [Fact]
    public void StateFolder_WithMissingHandler_ThrowsOnConstruction()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new IncompleteStateFolder(Registry));

        Assert.Contains("Unhandled events detected", ex.Message);
        Assert.Contains("MissingEvent", ex.Message);
        Assert.Contains("protected ValidationState When(MissingEvent e, ValidationState state)", ex.Message);
    }

    [Fact]
    public void StateFolder_WithValidateEventCoverageFalse_DoesNotThrow()
    {
        var folder = new NoValidationStateFolder(Registry);
        Assert.NotNull(folder);
    }

    [Fact]
    public void StateFolder_WithAllHandlers_DoesNotThrow()
    {
        var folder = new CompleteStateFolder(Registry);
        Assert.NotNull(folder);
    }

    [Fact]
    public void StateFolder_WithIgnoredEvents_DoesNotThrow()
    {
        var folder = new IgnoredEventsStateFolder(Registry);
        Assert.NotNull(folder);
    }

    [Fact]
    public void StateFolder_CallsCorrectHandler()
    {
        var folder = new CompleteStateFolder(Registry);
        var state = folder.InitialState();

        var handled = folder.Apply(state, new HandledEvent());

        Assert.True(handled.WasHandled);
    }

    [Fact]
    public void StateFolder_UnknownEvent_ReturnsStateUnchanged()
    {
        var folder = new NoValidationStateFolder(Registry);
        var state = folder.InitialState();

        var result = folder.Apply(state, new object());

        Assert.Same(state, result);
    }
}

// Test domain
[Aggregate("Validation")]
public record ValidationState(bool WasHandled = false);

[Event("Validation", "Handled", 1)]
public record HandledEvent;

[Event("Validation", "Missing", 1)]
public record MissingEvent;

// Incomplete: missing handler for MissingEvent
public class IncompleteStateFolder : StateFolder<ValidationState>
{
    public IncompleteStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override ValidationState InitialState() => new();

    protected ValidationState When(HandledEvent e, ValidationState state)
    {
        return state with { WasHandled = true };
    }
    // Missing: protected ValidationState When(MissingEvent e, ValidationState state)
}

// Complete: has all handlers
public class CompleteStateFolder : StateFolder<ValidationState>
{
    public CompleteStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override ValidationState InitialState() => new();

    protected ValidationState When(HandledEvent e, ValidationState state)
    {
        return state with { WasHandled = true };
    }

    protected ValidationState When(MissingEvent e, ValidationState state)
    {
        return state;
    }
}

// Validation disabled - can override on folder
[Aggregate("Validation", ValidateEventCoverage = false)]
public class NoValidationStateFolder : StateFolder<ValidationState>
{
    public NoValidationStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    public override ValidationState InitialState() => new();

    protected ValidationState When(HandledEvent e, ValidationState state)
    {
        return state with { WasHandled = true };
    }
    // Missing handler is OK because validation is disabled on folder
}

// Uses IgnoredEvents
public class IgnoredEventsStateFolder : StateFolder<ValidationState>
{
    public IgnoredEventsStateFolder(EventStore.TypeMetadata.ITypeMetadataRegistry registry) : base(registry) { }

    protected override ISet<Type> IgnoredEvents => new HashSet<Type> { typeof(MissingEvent) };

    public override ValidationState InitialState() => new();

    protected ValidationState When(HandledEvent e, ValidationState state)
    {
        return state with { WasHandled = true };
    }
    // MissingEvent is in IgnoredEvents, so no handler needed
}

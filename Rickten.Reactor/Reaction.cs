using Rickten.EventStore;
using Rickten.EventStore.TypeMetadata;
using Rickten.Projector;
using System.Reflection;

namespace Rickten.Reactor;

/// <summary>
/// Abstract base class for reactions with projection-based stream selection.
/// A reaction uses a projection to identify affected aggregate streams, then executes
/// a command against each selected stream when triggered by a matching event.
/// </summary>
/// <typeparam name="TView">The projection view type used to select target streams.</typeparam>
/// <typeparam name="TCommand">The type of the command to execute against target aggregates.</typeparam>
public abstract class Reaction<TView, TCommand>
{
    private readonly ReactionInfo _reactionInfo;

    protected Reaction(ITypeMetadataRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var implementationType = GetType();
        var metadata = registry.GetMetadataByType(implementationType);

        if (metadata == null)
        {
            throw new InvalidOperationException(
                $"Reaction type '{implementationType.Name}' is not registered in the TypeMetadataRegistry. " +
                $"Ensure the assembly containing this reaction is registered with TypeMetadataRegistryBuilder.");
        }

        if (metadata.AttributeInstance is not ReactionAttribute reactionAttr)
        {
            throw new InvalidOperationException(
                $"Reaction type '{implementationType.Name}' must be decorated with [Reaction] attribute.");
        }

        _reactionInfo = new ReactionInfo(reactionAttr.Name, reactionAttr.EventTypes, metadata.WireName);
    }

    /// <summary>
    /// Gets the reaction name from the [Reaction] attribute.
    /// </summary>
    public string ReactionName => _reactionInfo.Name;

    /// <summary>
    /// Gets the wire name of this reaction.
    /// Format: Reaction.{Name}.{ClassName}
    /// </summary>
    public string? WireName => _reactionInfo.WireName;

    /// <summary>
    /// Gets the event type filter from the [Reaction] attribute.
    /// </summary>
    public string[] EventTypeFilter => _reactionInfo.EventTypes;

    /// <summary>
    /// Gets the projection used to identify affected aggregate streams.
    /// The projection view is evaluated up to (and including) each trigger event.
    /// </summary>
    public abstract IProjection<TView> Projection { get; }

    /// <summary>
    /// Selects which aggregate streams should receive commands based on the projection view
    /// and the trigger event.
    /// </summary>
    /// <param name="view">The projection view representing state up to the trigger event.</param>
    /// <param name="trigger">The event that triggered this reaction.</param>
    /// <returns>Zero, one, or many stream identifiers to command. Empty if no streams are affected.</returns>
    protected abstract IEnumerable<StreamIdentifier> SelectStreams(TView view, StreamEvent trigger);

    /// <summary>
    /// Builds a command for a specific target stream.
    /// </summary>
    /// <param name="stream">The target aggregate stream identifier.</param>
    /// <param name="view">The projection view representing state up to the trigger event.</param>
    /// <param name="trigger">The event that triggered this reaction.</param>
    /// <returns>The command to execute against the target aggregate.</returns>
    protected abstract TCommand BuildCommand(StreamIdentifier stream, TView view, StreamEvent trigger);

    /// <summary>
    /// Determines if an event should trigger this reaction.
    /// Default implementation checks event type against EventTypeFilter.
    /// Override to add custom filtering logic.
    /// </summary>
    /// <param name="streamEvent">The event to evaluate.</param>
    /// <returns>True if the event should trigger this reaction.</returns>
    protected virtual bool ShouldProcess(StreamEvent streamEvent)
    {
        var eventType = streamEvent.Event.GetType();
        var eventAttr = eventType.GetCustomAttribute<EventStore.EventAttribute>();

        if (eventAttr == null)
        {
            return false;
        }

        var wireName = $"{eventAttr.Aggregate}.{eventAttr.Name}.v{eventAttr.Version}";
        return EventTypeFilter.Contains(wireName);
    }

    /// <summary>
    /// Processes a trigger event by evaluating the projection view and building commands
    /// for all selected streams. Internal use by ReactionRunner.
    /// </summary>
    internal IEnumerable<(StreamIdentifier Stream, TCommand Command)> Process(TView view, StreamEvent trigger)
    {
        if (!ShouldProcess(trigger))
        {
            throw new InvalidOperationException(
                $"Event type {trigger.Event.GetType().Name} does not match reaction filter.");
        }

        var streams = SelectStreams(view, trigger);

        foreach (var stream in streams)
        {
            var command = BuildCommand(stream, view, trigger);
            yield return (stream, command);
        }
    }
}

/// <summary>
/// Internal record to hold reaction metadata.
/// </summary>
internal sealed record ReactionInfo(string Name, string[] EventTypes, string? WireName);

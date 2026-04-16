using Rickten.EventStore;
using System.Reflection;

namespace Rickten.Projector;

/// <summary>
/// Abstract base class for projections with optional attribute-based filtering.
/// </summary>
/// <typeparam name="TView">The type of the read model view.</typeparam>
public abstract class Projection<TView> : IProjection<TView>
{
    private readonly Lazy<ProjectionInfo> _projectionInfo;

    protected Projection()
    {
        _projectionInfo = new Lazy<ProjectionInfo>(() =>
        {
            var implementationType = GetType();
            var attr = implementationType.GetCustomAttribute<ProjectionAttribute>();

            return new ProjectionInfo(
                attr?.Name ?? implementationType.Name,
                attr?.AggregateTypes,
                attr?.EventTypes);
        });
    }

    /// <summary>
    /// Gets the projection name from the [Projection] attribute or class name.
    /// </summary>
    public string ProjectionName => _projectionInfo.Value.Name;

    /// <summary>
    /// Gets the aggregate type filter from the [Projection] attribute, if configured.
    /// </summary>
    public string[]? AggregateTypeFilter => _projectionInfo.Value.AggregateTypes;

    /// <summary>
    /// Gets the event type filter from the [Projection] attribute, if configured.
    /// </summary>
    public string[]? EventTypeFilter => _projectionInfo.Value.EventTypes;

    /// <inheritdoc />
    public abstract TView InitialView();

    /// <inheritdoc />
    public TView Apply(TView view, StreamEvent streamEvent)
    {
        // Validate event against filters if configured
        if (!ShouldProcessEvent(streamEvent))
        {
            return view;
        }

        return ApplyEvent(view, streamEvent);
    }

    /// <summary>
    /// Apply a specific event to the view. Override this to handle your event types.
    /// Only called for events that pass the filter.
    /// </summary>
    /// <param name="view">The current view.</param>
    /// <param name="streamEvent">The event with full context.</param>
    /// <returns>The new view after applying the event.</returns>
    protected abstract TView ApplyEvent(TView view, StreamEvent streamEvent);

    private bool ShouldProcessEvent(StreamEvent streamEvent)
    {
        var info = _projectionInfo.Value;

        // Check aggregate type filter
        if (info.AggregateTypes != null && info.AggregateTypes.Length > 0)
        {
            var aggregateType = streamEvent.StreamPointer.Stream.StreamType;
            if (!info.AggregateTypes.Contains(aggregateType))
            {
                throw new InvalidOperationException(
                    $"Projection '{info.Name}' received event from aggregate '{aggregateType}' " +
                    $"but filter only allows: {string.Join(", ", info.AggregateTypes)}. " +
                    $"This indicates a mismatch between attribute filter and query.");
            }
        }

        // Check event type filter
        if (info.EventTypes != null && info.EventTypes.Length > 0 && streamEvent.Event != null)
        {
            var eventType = streamEvent.Event.GetType();
            var eventAttr = eventType.GetCustomAttribute<EventAttribute>();
            var eventName = eventAttr?.Name ?? eventType.Name;

            if (!info.EventTypes.Contains(eventName))
            {
                throw new InvalidOperationException(
                    $"Projection '{info.Name}' received event '{eventName}' " +
                    $"but filter only allows: {string.Join(", ", info.EventTypes)}. " +
                    $"This indicates a mismatch between attribute filter and query.");
            }
        }

        return true;
    }

    private record ProjectionInfo(string Name, string[]? AggregateTypes, string[]? EventTypes);
}

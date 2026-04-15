using Rickten.EventStore;

namespace Rickten.Projector;

/// <summary>
/// Defines how to project events into a read model view.
/// </summary>
/// <typeparam name="TView">The type of the read model view.</typeparam>
public interface IProjection<TView>
{
    /// <summary>
    /// Gets the initial view before any events are applied.
    /// </summary>
    /// <returns>The initial view state.</returns>
    TView InitialView();

    /// <summary>
    /// Applies a single event to the current view, producing a new view.
    /// The projection has access to the full StreamEvent including metadata.
    /// </summary>
    /// <param name="view">The current view state.</param>
    /// <param name="streamEvent">The event with full context (pointer, metadata).</param>
    /// <returns>A new view with the event applied.</returns>
    TView Apply(TView view, StreamEvent streamEvent);
}

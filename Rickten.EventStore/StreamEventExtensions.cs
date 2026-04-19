namespace Rickten.EventStore;

/// <summary>
/// Extension methods for working with StreamEvent and AppendEvent collections.
/// </summary>
public static class StreamEventExtensions
{
    /// <summary>
    /// Wraps raw event objects in AppendEvent with the specified metadata.
    /// </summary>
    /// <param name="events">The raw event objects to wrap.</param>
    /// <param name="metadata">The metadata to attach to each event.</param>
    /// <returns>A list of AppendEvent instances.</returns>
    public static List<AppendEvent> ToAppendEvent(this IReadOnlyList<object> events, IReadOnlyList<AppendMetadata> metadata)
        => [.. events.Select(e => new AppendEvent(e, metadata))];

    /// <summary>
    /// Gets the StreamPointer of the last event in the collection.
    /// </summary>
    /// <param name="events">The collection of stream events.</param>
    /// <returns>The StreamPointer of the last event.</returns>
    /// <exception cref="ArgumentException">Thrown when the collection is empty.</exception>
    public static StreamPointer LastVersion(this IReadOnlyList<StreamEvent> events)
    {
        if (events.Count == 0)
        {
            throw new ArgumentException("Cannot get last version from empty event list.", nameof(events));
        }

        return events[events.Count - 1].StreamPointer;
    }

    public static StreamIdentifier GetStreamIdentifier(this StreamEvent @event)
        => @event.StreamPointer.Stream;

    public static long GetVersion(this StreamEvent @event)
    => @event.StreamPointer.Version;

    public static bool IsOfStream(this StreamEvent @event, StreamIdentifier streamIdentifier)
        => @event.GetStreamIdentifier() == streamIdentifier;

    public static bool IsVersion(this StreamEvent @event, long expectedVersion)
    => @event.GetVersion() == expectedVersion;
}

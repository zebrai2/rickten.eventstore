namespace Rickten.EventStore;

/// <summary>
/// Represents an event that has been persisted to a stream.
/// </summary>
/// <param name="StreamPointer">The stream pointer indicating the stream and version of this event.</param>
/// <param name="GlobalPosition">The global position of this event across all streams.</param>
/// <param name="Event">The event data.</param>
/// <param name="Metadata">The metadata associated with this event.</param>
public sealed record StreamEvent(
    StreamPointer StreamPointer,
    long GlobalPosition,
    object Event,
    IReadOnlyList<EventMetadata> Metadata) : IComparable<StreamEvent>, IComparable<long>
{
    /// <summary>
    /// Compares this StreamEvent to another StreamEvent by version.
    /// Delegates to StreamPointer comparison.
    /// </summary>
    public int CompareTo(StreamEvent? other)
    {
        if (other is null) return 1;
        return StreamPointer.CompareTo(other.StreamPointer);
    }

    /// <summary>
    /// Compares this StreamEvent's version to a long value.
    /// </summary>
    public int CompareTo(long other) => StreamPointer.CompareTo(other);

    public static bool operator <(StreamEvent left, StreamEvent right) => left.CompareTo(right) < 0;
    public static bool operator <=(StreamEvent left, StreamEvent right) => left.CompareTo(right) <= 0;
    public static bool operator >(StreamEvent left, StreamEvent right) => left.CompareTo(right) > 0;
    public static bool operator >=(StreamEvent left, StreamEvent right) => left.CompareTo(right) >= 0;

    public static bool operator <(StreamEvent left, long right) => left.StreamPointer.Version < right;
    public static bool operator <=(StreamEvent left, long right) => left.StreamPointer.Version <= right;
    public static bool operator >(StreamEvent left, long right) => left.StreamPointer.Version > right;
    public static bool operator >=(StreamEvent left, long right) => left.StreamPointer.Version >= right;
    public static bool operator ==(StreamEvent left, long right) => left.StreamPointer.Version == right;
    public static bool operator !=(StreamEvent left, long right) => left.StreamPointer.Version != right;

    public static bool operator <(long left, StreamEvent right) => left < right.StreamPointer.Version;
    public static bool operator <=(long left, StreamEvent right) => left <= right.StreamPointer.Version;
    public static bool operator >(long left, StreamEvent right) => left > right.StreamPointer.Version;
    public static bool operator >=(long left, StreamEvent right) => left >= right.StreamPointer.Version;
    public static bool operator ==(long left, StreamEvent right) => left == right.StreamPointer.Version;
    public static bool operator !=(long left, StreamEvent right) => left != right.StreamPointer.Version;
}

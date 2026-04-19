namespace Rickten.EventStore;

/// <summary>
/// Represents a pointer to a specific version within a stream.
/// </summary>
/// <param name="Stream">The stream identifier.</param>
/// <param name="Version">
/// The current stream version (last written event version).
/// Version 0 indicates a new stream with no events written yet.
/// Version N means the stream has N events, and the next append will write version N+1.
/// </param>
public sealed record StreamPointer(
    StreamIdentifier Stream,
    long Version) : IComparable<StreamPointer>, IComparable<long>
{
    /// <summary>
    /// Compares this StreamPointer to another StreamPointer by version.
    /// Throws if the streams don't match.
    /// </summary>
    public int CompareTo(StreamPointer? other)
    {
        if (other is null) return 1;

        if (Stream != other.Stream)
        {
            throw new InvalidOperationException(
                $"Cannot compare versions from different streams: {Stream.StreamType}/{Stream.Identifier} vs {other.Stream.StreamType}/{other.Stream.Identifier}");
        }

        return Version.CompareTo(other.Version);
    }

    /// <summary>
    /// Compares this StreamPointer's version to a long value.
    /// </summary>
    public int CompareTo(long other) => Version.CompareTo(other);

    public static bool operator <(StreamPointer left, StreamPointer right) => left.CompareTo(right) < 0;
    public static bool operator <=(StreamPointer left, StreamPointer right) => left.CompareTo(right) <= 0;
    public static bool operator >(StreamPointer left, StreamPointer right) => left.CompareTo(right) > 0;
    public static bool operator >=(StreamPointer left, StreamPointer right) => left.CompareTo(right) >= 0;

    public static bool operator <(StreamPointer left, long right) => left.Version < right;
    public static bool operator <=(StreamPointer left, long right) => left.Version <= right;
    public static bool operator >(StreamPointer left, long right) => left.Version > right;
    public static bool operator >=(StreamPointer left, long right) => left.Version >= right;
    public static bool operator ==(StreamPointer left, long right) => left.Version == right;
    public static bool operator !=(StreamPointer left, long right) => left.Version != right;

    public static bool operator <(long left, StreamPointer right) => left < right.Version;
    public static bool operator <=(long left, StreamPointer right) => left <= right.Version;
    public static bool operator >(long left, StreamPointer right) => left > right.Version;
    public static bool operator >=(long left, StreamPointer right) => left >= right.Version;
    public static bool operator ==(long left, StreamPointer right) => left == right.Version;
    public static bool operator !=(long left, StreamPointer right) => left != right.Version;
}

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
    long Version)
{
    public StreamPointer WithVersion(long version)
    {
        return this with { Version = version };
    }
}

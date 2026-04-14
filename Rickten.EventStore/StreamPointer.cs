namespace Rickten.EventStore;

/// <summary>
/// Represents a pointer to a specific version within a stream.
/// </summary>
/// <param name="Stream">The stream identifier.</param>
/// <param name="Version">The version number within the stream. Version 0 indicates a new stream.</param>
public sealed record StreamPointer(
    StreamIdentifier Stream,
    long Version);

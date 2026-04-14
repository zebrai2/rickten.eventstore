namespace Rickten.EventStore;

/// <summary>
/// Represents a snapshot of a stream's state at a specific version.
/// </summary>
/// <param name="StreamPointer">The stream pointer indicating the stream and version of this snapshot.</param>
/// <param name="State">The snapshotted state.</param>
public sealed record Snapshot(
    StreamPointer StreamPointer,
    object State);

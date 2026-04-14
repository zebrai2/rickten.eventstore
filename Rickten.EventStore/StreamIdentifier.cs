namespace Rickten.EventStore;

/// <summary>
/// Uniquely identifies a stream by its type and identifier.
/// </summary>
/// <param name="StreamType">The type of the stream (e.g., aggregate type).</param>
/// <param name="Identifier">The unique identifier within the stream type.</param>
public sealed record StreamIdentifier(
    string StreamType, string Identifier)
{
    /// <summary>
    /// Implicitly converts a stream identifier to a stream pointer at version 0.
    /// </summary>
    public static implicit operator StreamPointer(StreamIdentifier identifier) =>
        new(identifier, 0);
}

namespace Rickten.EventStore;

/// <summary>
/// Exception thrown when a stream version conflict occurs during optimistic concurrency control.
/// </summary>
/// <param name="message">The error message.</param>
/// <param name="innerException">The inner exception that caused this exception.</param>
public sealed class StreamVersionConflictException(string? message = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// Gets the expected stream version that was provided.
    /// </summary>
    public StreamPointer? ExpectedVersion { get; init; }
    /// <summary>
    /// Gets the actual current version of the stream.
    /// </summary>
    public StreamPointer? ActualVersion { get; init; }
}

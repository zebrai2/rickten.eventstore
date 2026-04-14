namespace Rickten.EventStore;

/// <summary>
/// Exception thrown when a requested stream cannot be found.
/// </summary>
/// <param name="message">The error message.</param>
/// <param name="innerException">The inner exception that caused this exception.</param>
public sealed class StreamNotFoundException(string? message = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// Gets the identifier of the stream that was not found.
    /// </summary>
    public StreamIdentifier? StreamIdentifier { get; init; }
}

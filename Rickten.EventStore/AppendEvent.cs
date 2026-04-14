namespace Rickten.EventStore;

/// <summary>
/// Represents an event to be appended to a stream.
/// </summary>
/// <param name="Event">The event data to append.</param>
/// <param name="Metadata">Optional client metadata to associate with the event. The source will be automatically set to "Client".</param>
public sealed record AppendEvent(
    object Event,
    IReadOnlyList<AppendMetadata>? Metadata = null);

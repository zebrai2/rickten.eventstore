namespace Rickten.EventStore.TypeMetadata;

/// <summary>
/// Provides readonly lookup of type metadata for event sourcing types.
/// Built once at startup from explicitly registered assemblies.
/// </summary>
public interface ITypeMetadataRegistry
{
    /// <summary>
    /// Gets metadata for a CLR type.
    /// </summary>
    /// <param name="type">The CLR type to lookup.</param>
    /// <returns>The metadata if found; null otherwise.</returns>
    TypeMetadata? GetMetadataByType(Type type);

    /// <summary>
    /// Gets a CLR type from its wire/storage name.
    /// </summary>
    /// <param name="wireName">The wire name to lookup.</param>
    /// <returns>The CLR type if found; null otherwise.</returns>
    Type? GetTypeByWireName(string wireName);

    /// <summary>
    /// Gets all event types for a given aggregate name.
    /// </summary>
    /// <param name="aggregateName">The aggregate name.</param>
    /// <returns>A readonly collection of event types for the aggregate.</returns>
    IReadOnlyCollection<Type> GetEventTypesForAggregate(string aggregateName);

    /// <summary>
    /// Validates that all events have aggregates matching the expected stream type.
    /// </summary>
    /// <param name="events">The events to validate.</param>
    /// <param name="expectedStreamType">The expected stream type.</param>
    /// <exception cref="InvalidOperationException">Thrown when an event's aggregate doesn't match the stream type.</exception>
    void ValidateEventsForStream(IEnumerable<object> events, string expectedStreamType);
}

using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework.Entities;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Entity Framework Core implementation of <see cref="IEventStore"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="EventStore"/> class.
/// </remarks>
/// <param name="context">The database context.</param>
/// <param name="registry">The type metadata registry.</param>
/// <param name="serializer">The wire type serializer.</param>
public sealed class EventStore(
    EventStoreDbContext context, 
    ITypeMetadataRegistry registry,
    Serialization.WireTypeSerializer serializer) : IEventStore
{
    private readonly EventStoreDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly ITypeMetadataRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly Serialization.WireTypeSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));


    /// <inheritdoc />
    public async IAsyncEnumerable<StreamEvent> LoadAsync(
        StreamPointer fromVersion,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var events = _context.Events
            .Where(e => e.StreamType == fromVersion.Stream.StreamType
                     && e.StreamIdentifier == fromVersion.Stream.Identifier
                     && e.Version > fromVersion.Version)
            .OrderBy(e => e.Version)
            .AsAsyncEnumerable();

        await foreach (var entity in events.WithCancellation(cancellationToken))
        {
            yield return MapToStreamEvent(entity);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamEvent> LoadAllAsync(
        long fromGlobalPosition = 0,
        string[]? streamTypeFilter = null,
        string[]? eventsFilter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = _context.Events
            .Where(e => e.Id > fromGlobalPosition);

        if (streamTypeFilter?.Length > 0)
        {
            query = query.Where(e => streamTypeFilter.Contains(e.StreamType));
        }

        if (eventsFilter?.Length > 0)
        {
            query = query.Where(e => eventsFilter.Contains(e.EventType));
        }

        var events = query
            .OrderBy(e => e.Id)
            .AsAsyncEnumerable();

        await foreach (var entity in events.WithCancellation(cancellationToken))
        {
            yield return MapToStreamEvent(entity);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StreamEvent>> AppendAsync(
        StreamPointer expectedVersion,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return Array.Empty<StreamEvent>();
        }

        // Check current version (0 means new stream, no events written yet)
        var currentVersion = await _context.Events
            .Where(e => e.StreamType == expectedVersion.Stream.StreamType
                     && e.StreamIdentifier == expectedVersion.Stream.Identifier)
            .MaxAsync(e => (long?)e.Version, cancellationToken) ?? 0;

        if (currentVersion != expectedVersion.Version)
        {
            throw new StreamVersionConflictException(
                expectedVersion,
                new StreamPointer(expectedVersion.Stream, currentVersion),
                $"Stream version conflict for {expectedVersion.Stream.StreamType}/{expectedVersion.Stream.Identifier}. Expected {expectedVersion.Version}, actual {currentVersion}");
        }

        var appendedEvents = new List<StreamEvent>();
        // Events are 1-indexed; start from currentVersion + 1
        var version = currentVersion + 1;

        // Track the entities we're about to add so we can clean them up if the append fails
        var entitiesToAppend = new List<EventEntity>();

        // Generate a shared BatchId for all events in this append operation
        var batchId = Guid.NewGuid();

        foreach (var appendEvent in events)
        {
            // Validate that event aggregate matches stream type using the registry
            var eventType = appendEvent.Event.GetType();
            var eventMetadata = _registry.GetMetadataByType(eventType);
            if (eventMetadata?.AggregateName != null && eventMetadata.AggregateName != expectedVersion.Stream.StreamType)
            {
                throw new InvalidOperationException(
                    $"Event aggregate '{eventMetadata.AggregateName}' does not match stream type '{expectedVersion.Stream.StreamType}'. " +
                    $"Event type: {eventType.FullName}");
            }

            // Transform AppendMetadata to EventMetadata with Source="Client"
            // and add system metadata
            var metadata = new List<EventMetadata>();

            // Add client metadata first
            if (appendEvent.Metadata != null)
            {
                foreach (var clientMetadata in appendEvent.Metadata)
                {
                    metadata.Add(new EventMetadata(EventMetadataSource.Client, clientMetadata.Key, clientMetadata.Value));
                }
            }

            // Generate system EventId for this event
            var eventId = Guid.NewGuid();

            // Ensure CorrelationId: use caller-provided if present, otherwise generate
            var existingCorrelationId = appendEvent.Metadata?.FirstOrDefault(m => m.Key == EventMetadataKeys.CorrelationId);
            if (existingCorrelationId == null)
            {
                // No caller-provided CorrelationId, generate one
                metadata.Add(new EventMetadata(EventMetadataSource.System, EventMetadataKeys.CorrelationId, Guid.NewGuid()));
            }
            // Otherwise caller-provided CorrelationId is already in metadata with "Client" source

            // Add system metadata
            metadata.Add(new EventMetadata(EventMetadataSource.System, EventMetadataKeys.EventId, eventId));
            metadata.Add(new EventMetadata(EventMetadataSource.System, EventMetadataKeys.BatchId, batchId));
            metadata.Add(new EventMetadata(EventMetadataSource.System, EventMetadataKeys.Timestamp, DateTime.UtcNow));
            metadata.Add(new EventMetadata(EventMetadataSource.System, EventMetadataKeys.StreamVersion, version));

            var entity = new EventEntity
            {
                StreamType = expectedVersion.Stream.StreamType,
                StreamIdentifier = expectedVersion.Stream.Identifier,
                Version = version,
                EventType = _serializer.GetWireName(appendEvent.Event),
                EventData = _serializer.Serialize(appendEvent.Event),
                Metadata = _serializer.Serialize(metadata.ToArray()),
                CreatedAt = DateTime.UtcNow
            };

            _context.Events.Add(entity);
            entitiesToAppend.Add(entity);
            version++;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Detach the failed entities from the context to prevent them from
            // poisoning subsequent append attempts in the same scoped DbContext.
            // This enables same-scope retry after catching StreamVersionConflictException.
            foreach (var entity in entitiesToAppend)
            {
                _context.Entry(entity).State = EntityState.Detached;
            }

            // Re-query to get the actual current version
            var actualVersion = await _context.Events
                .Where(e => e.StreamType == expectedVersion.Stream.StreamType
                         && e.StreamIdentifier == expectedVersion.Stream.Identifier)
                .MaxAsync(e => (long?)e.Version, cancellationToken) ?? 0;

            throw new StreamVersionConflictException(
                expectedVersion,
                new StreamPointer(expectedVersion.Stream, actualVersion),
                $"Stream version conflict for {expectedVersion.Stream.StreamType}/{expectedVersion.Stream.Identifier}",
                ex);
        }

        // Load back with global positions (only the newly appended events)
        var loadedEvents = await _context.Events
            .Where(e => e.StreamType == expectedVersion.Stream.StreamType
                     && e.StreamIdentifier == expectedVersion.Stream.Identifier
                     && e.Version > expectedVersion.Version)
            .OrderBy(e => e.Version)
            .ToListAsync(cancellationToken);

        return loadedEvents.Select(MapToStreamEvent).ToList();
    }

    private StreamEvent MapToStreamEvent(EventEntity entity)
    {
        var streamPointer = new StreamPointer(
            new StreamIdentifier(entity.StreamType, entity.StreamIdentifier),
            entity.Version);

        var eventData = _serializer.Deserialize(entity.EventData, entity.EventType);
        var metadata = _serializer.DeserializeInfrastructure<EventMetadata[]>(entity.Metadata);

        return new StreamEvent(
            streamPointer,
            entity.GlobalPosition,
            eventData,
            metadata);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Check for unique constraint violation
        return ex.InnerException?.Message.Contains("IX_Events_Stream_Version") == true
            || ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true;
    }
}

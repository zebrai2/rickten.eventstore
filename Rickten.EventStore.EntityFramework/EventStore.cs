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

    private static List<EventMetadata> TransformAppendMetadata(IReadOnlyList<AppendMetadata>? appendMetadata)
    {
        var metadata = new List<EventMetadata>();

        if (appendMetadata == null) return metadata;

        foreach (var clientMetadata in appendMetadata)
        {
            // Skip CorrelationId - it will be re-added from the validated batch CorrelationId
            if (clientMetadata.IsCorrelationId()) continue;

            metadata.Add(new EventMetadata(EventMetadataSource.Client, clientMetadata.Key, clientMetadata.Value));
        }

        return metadata;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StreamEvent>> AppendAsync(
        StreamPointer expectedVersion,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0) return [];

        var expectedStream = expectedVersion.Stream;
        var expectedStreamVersion = expectedVersion.Version;

        var streamType = expectedStream.StreamType;
        var streamIdentifier = expectedStream.Identifier;

        var streamEvents = _context.Events;
        var currentVersion = await streamEvents.GetCurrentVersionAsync(
                                                    expectedStream,
                                                    cancellationToken);
        if (currentVersion != expectedVersion.Version)
        {
            throw new StreamVersionConflictException(
                expectedVersion, new StreamPointer(expectedStream, currentVersion),
                $"Stream version conflict for {streamType}/{streamIdentifier}. Expected {expectedStreamVersion}, actual {currentVersion}");
        }

        var entitiesToAppend = new List<EventEntity>();
        var version = ++currentVersion;
        var batchId = Guid.NewGuid();

        // Validate all events in batch have matching aggregates
        _registry.ValidateEventsForStream(events.GetEvents(), streamType);
        var batchMetadata = events.GetCorrelationIdStreamMetadata();

        foreach (var appendEvent in events)
        {
            var eventId = Guid.NewGuid();
            var metadata = TransformAppendMetadata(appendEvent.Metadata);

            metadata.AddCorrelationId(batchMetadata);
            metadata.AddSystemMetadata(version, batchId, eventId);

            var entity = new EventEntity
            {
                StreamType = streamType,
                StreamIdentifier = streamIdentifier,
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
                .Where(e => e.StreamType == streamType
                         && e.StreamIdentifier == streamIdentifier)
                .MaxAsync(e => (long?)e.Version, cancellationToken) ?? 0;

            throw new StreamVersionConflictException(
                expectedVersion,
                new StreamPointer(expectedStream, actualVersion),
                $"Stream version conflict for {streamType}/{streamIdentifier}",
                ex);
        }

        // Load back with global positions (only the newly appended events)
        var loadedEvents = await _context.Events
            .Where(e => e.StreamType == streamType
                     && e.StreamIdentifier == streamIdentifier
                     && e.Version > expectedStreamVersion)
            .OrderBy(e => e.Version)
            .ToListAsync(cancellationToken);

        return [.. loadedEvents.Select(MapToStreamEvent)];
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

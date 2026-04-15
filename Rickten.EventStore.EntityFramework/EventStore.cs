using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework.Entities;
using Rickten.EventStore.EntityFramework.Serialization;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Entity Framework Core implementation of <see cref="IEventStore"/>.
/// </summary>
public sealed class EventStore : IEventStore
{
    private readonly EventStoreDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventStore"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    public EventStore(EventStoreDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamEvent> LoadAsync(
        StreamPointer fromVersion,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var events = _context.Events
            .Where(e => e.StreamType == fromVersion.Stream.StreamType
                     && e.StreamIdentifier == fromVersion.Stream.Identifier
                     && e.Version >= fromVersion.Version)
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
            .Where(e => e.Id >= fromGlobalPosition);

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
                $"Stream version conflict for {expectedVersion.Stream.StreamType}/{expectedVersion.Stream.Identifier}. Expected {expectedVersion.Version}, actual {currentVersion}")
            {
                ExpectedVersion = expectedVersion,
                ActualVersion = new StreamPointer(expectedVersion.Stream, currentVersion)
            };
        }

        var appendedEvents = new List<StreamEvent>();
        // Events are 1-indexed; start from currentVersion + 1
        var version = currentVersion + 1;

        foreach (var appendEvent in events)
        {
            // Validate that event aggregate matches stream type
            var eventType = appendEvent.Event.GetType();
            var eventAttribute = eventType.GetCustomAttribute<EventAttribute>();
            if (eventAttribute != null && eventAttribute.Aggregate != expectedVersion.Stream.StreamType)
            {
                throw new InvalidOperationException(
                    $"Event aggregate '{eventAttribute.Aggregate}' does not match stream type '{expectedVersion.Stream.StreamType}'. " +
                    $"Event type: {eventType.FullName}");
            }

            // Transform AppendMetadata to EventMetadata with Source="Client"
            // and add system metadata
            var metadata = new List<EventMetadata>();

            // Add client metadata
            if (appendEvent.Metadata != null)
            {
                foreach (var clientMetadata in appendEvent.Metadata)
                {
                    metadata.Add(new EventMetadata("Client", clientMetadata.Key, clientMetadata.Value));
                }
            }

            // Add system metadata
            metadata.Add(new EventMetadata("System", "Timestamp", DateTime.UtcNow));
            metadata.Add(new EventMetadata("System", "StreamVersion", version));

            var entity = new EventEntity
            {
                StreamType = expectedVersion.Stream.StreamType,
                StreamIdentifier = expectedVersion.Stream.Identifier,
                Version = version,
                EventType = EventSerializer.GetTypeName(appendEvent.Event),
                EventData = EventSerializer.Serialize(appendEvent.Event),
                Metadata = EventSerializer.Serialize(metadata.ToArray()),
                CreatedAt = DateTime.UtcNow
            };

            _context.Events.Add(entity);
            version++;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new StreamVersionConflictException(
                $"Stream version conflict for {expectedVersion.Stream.StreamType}/{expectedVersion.Stream.Identifier}",
                ex)
            {
                ExpectedVersion = expectedVersion
            };
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

    private static StreamEvent MapToStreamEvent(EventEntity entity)
    {
        var streamPointer = new StreamPointer(
            new StreamIdentifier(entity.StreamType, entity.StreamIdentifier),
            entity.Version);

        var eventData = EventSerializer.Deserialize(entity.EventData, entity.EventType);
        var metadata = EventSerializer.Deserialize<EventMetadata[]>(entity.Metadata);

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

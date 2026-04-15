using Rickten.EventStore;

namespace Rickten.Projector.Tests;

internal class InMemoryEventStore : IEventStore
{
    private readonly List<(StreamEvent Event, long GlobalPosition)> _events = new();
    private readonly Dictionary<StreamIdentifier, long> _streamVersions = new();
    private long _globalPosition = 0;

    public IAsyncEnumerable<StreamEvent> LoadAsync(
        StreamPointer fromVersion,
        CancellationToken cancellationToken = default)
    {
        return _events
            .Where(e => e.Event.StreamPointer.Stream == fromVersion.Stream && e.Event.StreamPointer.Version >= fromVersion.Version)
            .Select(e => e.Event)
            .ToAsyncEnumerable();
    }

    public IAsyncEnumerable<StreamEvent> LoadAllAsync(
        long fromGlobalPosition = 0,
        string[]? streamTypeFilter = null,
        string[]? eventsFilter = null,
        CancellationToken cancellationToken = default)
    {
        var filtered = _events.Where(e => e.GlobalPosition >= fromGlobalPosition);

        if (streamTypeFilter != null && streamTypeFilter.Length > 0)
        {
            filtered = filtered.Where(e => streamTypeFilter.Contains(e.Event.StreamPointer.Stream.StreamType));
        }

        if (eventsFilter != null && eventsFilter.Length > 0)
        {
            filtered = filtered.Where(e => e.Event.Event != null && eventsFilter.Contains(e.Event.Event.GetType().Name));
        }

        return filtered.Select(e => e.Event).ToAsyncEnumerable();
    }

    public Task<IReadOnlyList<StreamEvent>> AppendAsync(
        StreamPointer expectedVersion,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default)
    {
        var streamId = expectedVersion.Stream;
        var currentVersion = _streamVersions.GetValueOrDefault(streamId, 0);

        if (currentVersion != expectedVersion.Version)
        {
            throw new StreamVersionConflictException($"Version conflict on stream {streamId}")
            {
                ExpectedVersion = expectedVersion,
                ActualVersion = new StreamPointer(streamId, currentVersion)
            };
        }

        var appendedEvents = new List<StreamEvent>();

        foreach (var evt in events)
        {
            currentVersion++;
            _globalPosition++;
            var streamEvent = new StreamEvent(
                new StreamPointer(streamId, currentVersion),
                evt.Event,
                null);
            _events.Add((streamEvent, _globalPosition));
            appendedEvents.Add(streamEvent);
        }

        _streamVersions[streamId] = currentVersion;
        return Task.FromResult<IReadOnlyList<StreamEvent>>(appendedEvents);
    }

    public Task<StreamEvent[]> AppendAsync(
        StreamIdentifier streamId,
        long expectedVersion,
        params object[] events)
    {
        var appendEvents = events.Select(e => new AppendEvent(e)).ToList();
        var pointer = new StreamPointer(streamId, expectedVersion);
        var result = AppendAsync(pointer, appendEvents).Result;
        return Task.FromResult(result.ToArray());
    }
}

internal class InMemoryProjectionStore : IProjectionStore
{
    private readonly Dictionary<string, object> _projections = new();

    public Task<EventStore.Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        CancellationToken cancellationToken = default)
    {
        if (_projections.TryGetValue(projectionKey, out var value) && value is EventStore.Projection<TState> projection)
        {
            return Task.FromResult<EventStore.Projection<TState>?>(projection);
        }
        return Task.FromResult<EventStore.Projection<TState>?>(null);
    }

    public Task SaveProjectionAsync<TState>(
        string projectionKey,
        long globalPosition,
        TState state,
        CancellationToken cancellationToken = default)
    {
        _projections[projectionKey] = new EventStore.Projection<TState>(state, globalPosition);
        return Task.CompletedTask;
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }
}

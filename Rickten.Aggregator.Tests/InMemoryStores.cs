using Rickten.EventStore;

namespace Rickten.Aggregator.Tests;

public class InMemoryEventStore : IEventStore
{
    private readonly Dictionary<string, List<StreamEvent>> _streams = new();
    private readonly object _lock = new();
    private long _globalPosition = 0;

    public async Task<IReadOnlyList<StreamEvent>> AppendAsync(
        StreamPointer pointer,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        lock (_lock)
        {
            var key = GetKey(pointer.Stream);
            if (!_streams.ContainsKey(key))
            {
                _streams[key] = new List<StreamEvent>();
            }

            var stream = _streams[key];
            var expectedVersion = pointer.Version;

            // Validate optimistic concurrency
            if (expectedVersion != 0 && stream.Count + 1 != expectedVersion)
            {
                throw new InvalidOperationException("Concurrency conflict");
            }

            var appended = new List<StreamEvent>();
            var version = stream.Count;

            foreach (var appendEvent in events)
            {
                version++;
                _globalPosition++;
                var metadata = appendEvent.Metadata?
                    .Select(m => new EventMetadata("Test", m.Key, m.Value))
                    .ToList() ?? [];

                var streamEvent = new StreamEvent(
                    new StreamPointer(pointer.Stream, version),
                    _globalPosition,
                    appendEvent.Event,
                    metadata);

                stream.Add(streamEvent);
                appended.Add(streamEvent);
            }

            return appended;
        }
    }

    public async IAsyncEnumerable<StreamEvent> LoadAsync(
        StreamPointer fromPosition,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        List<StreamEvent> events;
        lock (_lock)
        {
            var key = GetKey(fromPosition.Stream);
            if (!_streams.TryGetValue(key, out var stream))
            {
                yield break;
            }

            events = stream.Skip((int)fromPosition.Version).ToList();
        }

        foreach (var @event in events)
        {
            yield return @event;
        }
    }

    public async IAsyncEnumerable<StreamEvent> LoadAllAsync(
        long fromVersion = 0,
        string[]? streamTypes = null,
        string[]? eventTypes = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        List<StreamEvent> allEvents;
        lock (_lock)
        {
            allEvents = _streams.Values
                .SelectMany(stream => stream)
                .Where(e => e.GlobalPosition >= fromVersion)
                .OrderBy(e => e.GlobalPosition)
                .ToList();
        }

        foreach (var @event in allEvents)
        {
            yield return @event;
        }
    }

    private static string GetKey(StreamIdentifier identifier) =>
        $"{identifier.StreamType}/{identifier.Identifier}";
}

public class InMemorySnapshotStore : ISnapshotStore
{
    private readonly Dictionary<string, Snapshot> _snapshots = new();
    private readonly object _lock = new();

    public Task<Snapshot?> LoadSnapshotAsync(
        StreamIdentifier streamIdentifier,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var key = GetKey(streamIdentifier);
            return Task.FromResult(_snapshots.TryGetValue(key, out var snapshot) ? snapshot : null);
        }
    }

    public Task SaveSnapshotAsync(
        StreamPointer streamPointer,
        object state,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var key = GetKey(streamPointer.Stream);
            _snapshots[key] = new Snapshot(streamPointer, state);
        }

        return Task.CompletedTask;
    }

    private static string GetKey(StreamIdentifier identifier) =>
        $"{identifier.StreamType}/{identifier.Identifier}";
}

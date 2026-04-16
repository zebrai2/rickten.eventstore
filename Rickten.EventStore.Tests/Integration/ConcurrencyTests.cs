using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Rickten.EventStore.Tests.Integration;

[Event("Account", "Deposited", 1)]
public record AccountDepositedEvent(decimal Amount);

[Event("Account", "Withdrawn", 1)]
public record AccountWithdrawnEvent(decimal Amount);

/// <summary>
/// Comprehensive concurrency tests that rely on actual database constraint enforcement.
/// Tests optimistic concurrency control, race conditions, and transaction isolation.
/// </summary>
public class ConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventStoreDbContext> _options;

    public ConcurrencyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new EventStoreDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    private EventStoreDbContext CreateContext() => new EventStoreDbContext(_options);
    private EntityFramework.EventStore CreateEventStore() => new EntityFramework.EventStore(CreateContext());

    [Fact]
    public async Task OptimisticConcurrency_DetectsVersionConflict()
    {
        // Classic optimistic concurrency scenario:
        // 1. Two clients read the same stream version
        // 2. Both try to append at that version
        // 3. Only one should succeed

        var streamId = new StreamIdentifier("Account", "acc-001");
        var store1 = CreateEventStore();
        var store2 = CreateEventStore();

        // Initial state: account has version 0 (no events)
        var currentVersion = 0L;

        // Client 1 reads current version and prepares to append
        var client1Pointer = new StreamPointer(streamId, currentVersion);
        var client1Event = new AppendEvent(new AccountDepositedEvent(100m), null);

        // Client 2 also reads same version and prepares to append
        var client2Pointer = new StreamPointer(streamId, currentVersion);
        var client2Event = new AppendEvent(new AccountDepositedEvent(200m), null);

        // Client 1 appends successfully
        var result1 = await store1.AppendAsync(client1Pointer, new List<AppendEvent> { client1Event });
        Assert.Single(result1);
        Assert.Equal(1, result1[0].StreamPointer.Version);

        // Client 2 append should fail - version conflict
        var exception = await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
        {
            await store2.AppendAsync(client2Pointer, new List<AppendEvent> { client2Event });
        });

        Assert.Equal(currentVersion, exception.ExpectedVersion.Version);
        Assert.Equal(1, exception.ActualVersion.Version);
    }

    [Fact]
    public async Task OptimisticConcurrency_Client2CanRetry()
    {
        // Demonstrates the retry pattern after optimistic concurrency failure

        var streamId = new StreamIdentifier("Account", "acc-002");
        var store1 = CreateEventStore();
        var store2 = CreateEventStore();

        // Both clients start from version 0
        await store1.AppendAsync(
            new StreamPointer(streamId, 0),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(100m), null) });

        // Client 2 tries at version 0, fails
        try
        {
            await store2.AppendAsync(
                new StreamPointer(streamId, 0),
                new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(200m), null) });
            Assert.Fail("Should have thrown");
        }
        catch (StreamVersionConflictException ex)
        {
            // Client 2 retries with correct version from exception
            var retryStore = CreateEventStore();
            var result = await retryStore.AppendAsync(
                new StreamPointer(streamId, ex.ActualVersion.Version),
                new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(200m), null) });

            Assert.Single(result);
            Assert.Equal(2, result[0].StreamPointer.Version);
        }

        // Verify both events are now in the stream
        var readStore = CreateEventStore();
        var events = new List<StreamEvent>();
        await foreach (var evt in readStore.LoadAsync(new StreamPointer(streamId, 0)))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task RaceCondition_MultipleThreads_OnlyOneWins()
    {
        // Simulate multiple threads racing to append to the same stream
        var streamId = new StreamIdentifier("Account", "acc-race");
        var threadCount = 10;
        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();

        var tasks = Enumerable.Range(0, threadCount).Select(async i =>
        {
            try
            {
                var store = CreateEventStore();
                await store.AppendAsync(
                    new StreamPointer(streamId, 0),
                    new List<AppendEvent> 
                    { 
                        new AppendEvent(new AccountDepositedEvent(i * 100m), null) 
                    });
                results.Add(true); // Success
                return true;
            }
            catch (StreamVersionConflictException)
            {
                results.Add(false); // Failed due to conflict
                return false;
            }
        });

        await Task.WhenAll(tasks);

        // Exactly one should succeed
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(threadCount - 1, results.Count(r => !r));

        // Verify only one event in database
        var readStore = CreateEventStore();
        var events = new List<StreamEvent>();
        await foreach (var evt in readStore.LoadAsync(new StreamPointer(streamId, 0)))
        {
            events.Add(evt);
        }
        Assert.Single(events);
    }

    [Fact]
    public async Task SequentialAppends_MaintainVersionOrder()
    {
        // Verify that sequential appends maintain strict version ordering
        var streamId = new StreamIdentifier("Account", "acc-sequential");
        var store = CreateEventStore();
        var appendCount = 20;

        for (int i = 0; i < appendCount; i++)
        {
            var result = await store.AppendAsync(
                new StreamPointer(streamId, i),
                new List<AppendEvent> 
                { 
                    new AppendEvent(new AccountDepositedEvent(i * 10m), null) 
                });

            Assert.Single(result);
            Assert.Equal(i + 1, result[0].StreamPointer.Version);
        }

        // Verify all events are present and ordered
        var events = new List<StreamEvent>();
        await foreach (var evt in store.LoadAsync(new StreamPointer(streamId, 0)))
        {
            events.Add(evt);
        }

        Assert.Equal(appendCount, events.Count);
        for (int i = 0; i < appendCount; i++)
        {
            Assert.Equal(i + 1, events[i].StreamPointer.Version);
        }
    }

    [Fact]
    public async Task BatchAppend_AllOrNothing_OnVersionConflict()
    {
        // When appending multiple events, if version is wrong, none should be persisted
        var streamId = new StreamIdentifier("Account", "acc-batch");
        var store = CreateEventStore();

        // Create initial state
        await store.AppendAsync(
            new StreamPointer(streamId, 0),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(100m), null) });

        // Try to append batch at wrong version
        var batchEvents = new List<AppendEvent>
        {
            new AppendEvent(new AccountDepositedEvent(50m), null),
            new AppendEvent(new AccountDepositedEvent(75m), null),
            new AppendEvent(new AccountDepositedEvent(25m), null)
        };

        await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
        {
            await store.AppendAsync(new StreamPointer(streamId, 0), batchEvents);
        });

        // Verify no partial data was persisted
        var events = new List<StreamEvent>();
        await foreach (var evt in store.LoadAsync(new StreamPointer(streamId, 0)))
        {
            events.Add(evt);
        }

        Assert.Single(events); // Only the initial event
        Assert.Equal(1, events[0].StreamPointer.Version);
    }

    [Fact]
    public async Task IsolatedStreams_NoCrossStreamInterference()
    {
        // Verify that version conflicts in one stream don't affect other streams
        var stream1 = new StreamIdentifier("Account", "acc-isolated-1");
        var stream2 = new StreamIdentifier("Account", "acc-isolated-2");

        var store1 = CreateEventStore();
        var store2 = CreateEventStore();

        // Append to stream 1
        await store1.AppendAsync(
            new StreamPointer(stream1, 0),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(100m), null) });

        // Create conflict in stream 1
        await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
        {
            await store2.AppendAsync(
                new StreamPointer(stream1, 0),
                new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(200m), null) });
        });

        // Stream 2 should be completely unaffected
        var result = await store1.AppendAsync(
            new StreamPointer(stream2, 0),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(300m), null) });

        Assert.Single(result);
        Assert.Equal(1, result[0].StreamPointer.Version);
    }

    [Fact]
    public async Task VersionCheck_ReadsCommittedData()
    {
        // Verify that version checks read the latest committed data
        var streamId = new StreamIdentifier("Account", "acc-read-committed");

        // Store 1 writes
        var store1 = CreateEventStore();
        await store1.AppendAsync(
            new StreamPointer(streamId, 0),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(100m), null) });

        // Store 2 should see the committed version
        var store2 = CreateEventStore();
        var exception = await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
        {
            await store2.AppendAsync(
                new StreamPointer(streamId, 0),
                new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(200m), null) });
        });

        Assert.Equal(1, exception.ActualVersion.Version);
    }

    [Fact]
    public async Task ConcurrentDifferentVersions_CorrectlyOrdered()
    {
        // Multiple clients appending at different (correct) versions should all succeed
        var streamId = new StreamIdentifier("Account", "acc-concurrent-ordered");
        var store = CreateEventStore();

        // Create initial state with 5 events
        for (int i = 0; i < 5; i++)
        {
            await store.AppendAsync(
                new StreamPointer(streamId, i),
                new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(i * 10m), null) });
        }

        // Now have 3 clients append at their correct versions
        var store6 = CreateEventStore();
        var store7 = CreateEventStore();
        var store8 = CreateEventStore();

        var task6 = store6.AppendAsync(
            new StreamPointer(streamId, 5),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(60m), null) });

        var task7 = store7.AppendAsync(
            new StreamPointer(streamId, 6),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(70m), null) });

        var task8 = store8.AppendAsync(
            new StreamPointer(streamId, 7),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(80m), null) });

        // Wait for all tasks
        await Task.WhenAll(task6, task7, task8);

        // All should succeed because they're at correct sequential versions
        Assert.Equal(6, task6.Result[0].StreamPointer.Version);
        Assert.Equal(7, task7.Result[0].StreamPointer.Version);
        Assert.Equal(8, task8.Result[0].StreamPointer.Version);

        // Verify all 8 events are present
        var events = new List<StreamEvent>();
        await foreach (var evt in store.LoadAsync(new StreamPointer(streamId, 0)))
        {
            events.Add(evt);
        }
        Assert.Equal(8, events.Count);
    }

    [Fact]
    public async Task StaleRead_DetectedOnWrite()
    {
        // Simulate a scenario where a client has a stale view of the stream
        var streamId = new StreamIdentifier("Account", "acc-stale");

        // Time T0: Initial state
        var storeT0 = CreateEventStore();
        await storeT0.AppendAsync(
            new StreamPointer(streamId, 0),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(100m), null) });

        // Time T1: Client A reads stream (sees version 1)
        var clientAStore = CreateEventStore();
        var clientAEvents = new List<StreamEvent>();
        await foreach (var evt in clientAStore.LoadAsync(new StreamPointer(streamId, 0)))
        {
            clientAEvents.Add(evt);
        }
        var clientAVersion = clientAEvents.Last().StreamPointer.Version; // Version 1

        // Time T2: Client B writes (version becomes 2)
        var clientBStore = CreateEventStore();
        await clientBStore.AppendAsync(
            new StreamPointer(streamId, 1),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(200m), null) });

        // Time T3: Client A tries to write based on stale version 1
        await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
        {
            await clientAStore.AppendAsync(
                new StreamPointer(streamId, clientAVersion),
                new List<AppendEvent> { new AppendEvent(new AccountWithdrawnEvent(50m), null) });
        });

        // Client A must re-read and retry
        var clientARetryStore = CreateEventStore();
        var currentEvents = new List<StreamEvent>();
        await foreach (var evt in clientARetryStore.LoadAsync(new StreamPointer(streamId, 0)))
        {
            currentEvents.Add(evt);
        }

        var currentVersion = currentEvents.Last().StreamPointer.Version; // Version 2
        await clientARetryStore.AppendAsync(
            new StreamPointer(streamId, currentVersion),
            new List<AppendEvent> { new AppendEvent(new AccountWithdrawnEvent(50m), null) });

        // Verify final state
        var finalStore = CreateEventStore();
        var finalEvents = new List<StreamEvent>();
        await foreach (var evt in finalStore.LoadAsync(new StreamPointer(streamId, 0)))
        {
            finalEvents.Add(evt);
        }

        Assert.Equal(3, finalEvents.Count);
    }

    [Fact]
    public async Task UniqueConstraint_EnforcedAtDatabaseLevel()
    {
        // This test verifies the unique index is actually enforced by the database
        // by attempting to insert duplicate (StreamType, StreamIdentifier, Version) directly

        var streamId = new StreamIdentifier("Account", "acc-unique-constraint");

        // Normal append through event store
        var store = CreateEventStore();
        await store.AppendAsync(
            new StreamPointer(streamId, 0),
            new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(100m), null) });

        // Attempting another append at same version should fail
        await Assert.ThrowsAsync<StreamVersionConflictException>(async () =>
        {
            var store2 = CreateEventStore();
            await store2.AppendAsync(
                new StreamPointer(streamId, 0),
                new List<AppendEvent> { new AppendEvent(new AccountDepositedEvent(200m), null) });
        });
    }
}

using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore;
using Rickten.EventStore.TypeMetadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rickten.EventStore.Tests.Integration;

/// <summary>
/// Integration tests for trace identity metadata generation (EventId, CorrelationId, BatchId, CausationId).
/// These tests verify that the event store properly generates and persists trace identity metadata.
/// </summary>
public abstract class TraceIdentityIntegrationTestsBase
{
    private static readonly ITypeMetadataRegistry Registry = TestTypeMetadataRegistry.Create();

    protected abstract string AggregateType { get; }
    protected abstract void SkipIfNotAvailable();
    protected abstract EventStoreDbContext CreateContext();
    protected abstract object CreateTestEvent(string name);

    private EntityFramework.EventStore CreateEventStore() => new EntityFramework.EventStore(CreateContext(), Registry, new WireTypeSerializer(Registry));

    [SkippableFact]
    public async Task AppendAsync_Generates_EventId_For_Each_Event()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();
        var pointer = new StreamPointer(new StreamIdentifier(AggregateType, "eventid-test"), 0);

        var events = new List<AppendEvent>
        {
            new AppendEvent(CreateTestEvent("Event1"), null),
            new AppendEvent(CreateTestEvent("Event2"), null),
            new AppendEvent(CreateTestEvent("Event3"), null)
        };

        var result = await store.AppendAsync(pointer, events);

        Assert.Equal(3, result.Count);

        // Each event should have a unique EventId
        var eventId1 = result[0].Metadata.GetEventId();
        var eventId2 = result[1].Metadata.GetEventId();
        var eventId3 = result[2].Metadata.GetEventId();

        Assert.NotNull(eventId1);
        Assert.NotNull(eventId2);
        Assert.NotNull(eventId3);

        Assert.NotEqual(eventId1, eventId2);
        Assert.NotEqual(eventId2, eventId3);
        Assert.NotEqual(eventId1, eventId3);

        // EventId should have System source
        var eventIdMetadata = result[0].Metadata.FirstOrDefault(m => m.Key == EventMetadataKeys.EventId);
        Assert.NotNull(eventIdMetadata);
        Assert.Equal("System", eventIdMetadata.Source);
    }

    [SkippableFact]
    public async Task AppendAsync_Generates_Shared_BatchId_For_All_Events()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();
        var pointer = new StreamPointer(new StreamIdentifier(AggregateType, "batchid-test"), 0);

        var events = new List<AppendEvent>
        {
            new AppendEvent(CreateTestEvent("Event1"), null),
            new AppendEvent(CreateTestEvent("Event2"), null),
            new AppendEvent(CreateTestEvent("Event3"), null)
        };

        var result = await store.AppendAsync(pointer, events);

        Assert.Equal(3, result.Count);

        // All events should share the same BatchId
        var batchId1 = result[0].Metadata.GetBatchId();
        var batchId2 = result[1].Metadata.GetBatchId();
        var batchId3 = result[2].Metadata.GetBatchId();

        Assert.NotNull(batchId1);
        Assert.Equal(batchId1, batchId2);
        Assert.Equal(batchId2, batchId3);

        // BatchId should have System source
        var batchIdMetadata = result[0].Metadata.FirstOrDefault(m => m.Key == EventMetadataKeys.BatchId);
        Assert.NotNull(batchIdMetadata);
        Assert.Equal("System", batchIdMetadata.Source);
    }

    [SkippableFact]
    public async Task AppendAsync_Different_Batches_Have_Different_BatchIds()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();
        var streamId = new StreamIdentifier(AggregateType, "multi-batch-test");

        // First batch
        var result1 = await store.AppendAsync(
            new StreamPointer(streamId, 0),
            new List<AppendEvent> { new AppendEvent(CreateTestEvent("Event1"), null) });

        // Second batch
        var result2 = await store.AppendAsync(
            new StreamPointer(streamId, 1),
            new List<AppendEvent> { new AppendEvent(CreateTestEvent("Event2"), null) });

        var batchId1 = result1[0].Metadata.GetBatchId();
        var batchId2 = result2[0].Metadata.GetBatchId();

        Assert.NotNull(batchId1);
        Assert.NotNull(batchId2);
        Assert.NotEqual(batchId1, batchId2);
    }

    [SkippableFact]
    public async Task AppendAsync_Generates_CorrelationId_When_Not_Provided()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();
        var pointer = new StreamPointer(new StreamIdentifier(AggregateType, "corr-gen-test"), 0);

        var events = new List<AppendEvent>
        {
            new AppendEvent(CreateTestEvent("Event1"), null)
        };

        var result = await store.AppendAsync(pointer, events);

        Assert.Single(result);

        var correlationId = result[0].Metadata.GetCorrelationId();
        Assert.NotNull(correlationId);

        // System-generated CorrelationId should have System source
        var corrMetadata = result[0].Metadata.FirstOrDefault(m => m.Key == EventMetadataKeys.CorrelationId);
        Assert.NotNull(corrMetadata);
        Assert.Equal("System", corrMetadata.Source);
    }

    [SkippableFact]
    public async Task AppendAsync_Uses_Provided_CorrelationId()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();
        var pointer = new StreamPointer(new StreamIdentifier(AggregateType, "corr-provided-test"), 0);

        var providedCorrelationId = Guid.NewGuid();
        var events = new List<AppendEvent>
        {
            new AppendEvent(CreateTestEvent("Event1"), new List<AppendMetadata>
            {
                new AppendMetadata(EventMetadataKeys.CorrelationId, providedCorrelationId)
            })
        };

        var result = await store.AppendAsync(pointer, events);

        Assert.Single(result);

        var correlationId = result[0].Metadata.GetCorrelationId();
        Assert.Equal(providedCorrelationId, correlationId);

        // Client-provided CorrelationId should have Client source
        var corrMetadata = result[0].Metadata.FirstOrDefault(m => m.Key == EventMetadataKeys.CorrelationId);
        Assert.NotNull(corrMetadata);
        Assert.Equal("Client", corrMetadata.Source);
    }

    [SkippableFact]
    public async Task AppendAsync_Preserves_Provided_CausationId()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();
        var pointer = new StreamPointer(new StreamIdentifier(AggregateType, "causation-test"), 0);

        var providedCausationId = Guid.NewGuid();
        var events = new List<AppendEvent>
        {
            new AppendEvent(CreateTestEvent("Event1"), new List<AppendMetadata>
            {
                new AppendMetadata(EventMetadataKeys.CausationId, providedCausationId)
            })
        };

        var result = await store.AppendAsync(pointer, events);

        Assert.Single(result);

        var causationId = result[0].Metadata.GetCausationId();
        Assert.Equal(providedCausationId, causationId);

        // Client-provided CausationId should have Client source
        var causationMetadata = result[0].Metadata.FirstOrDefault(m => m.Key == EventMetadataKeys.CausationId);
        Assert.NotNull(causationMetadata);
        Assert.Equal("Client", causationMetadata.Source);
    }

    [SkippableFact]
    public async Task LoadAsync_Preserves_Trace_Metadata_After_Round_Trip()
    {
        SkipIfNotAvailable();

        var store = CreateEventStore();
        var streamId = new StreamIdentifier(AggregateType, "roundtrip-test");
        var pointer = new StreamPointer(streamId, 0);

        var providedCorrelationId = Guid.NewGuid();
        var providedCausationId = Guid.NewGuid();

        var events = new List<AppendEvent>
        {
            new AppendEvent(CreateTestEvent("Event1"), new List<AppendMetadata>
            {
                new AppendMetadata(EventMetadataKeys.CorrelationId, providedCorrelationId),
                new AppendMetadata(EventMetadataKeys.CausationId, providedCausationId)
            })
        };

        await store.AppendAsync(pointer, events);

        // Load events back
        var loadedEvents = new List<StreamEvent>();
        await foreach (var evt in store.LoadAsync(pointer))
        {
            loadedEvents.Add(evt);
        }

        Assert.Single(loadedEvents);
        var loaded = loadedEvents[0];

        // Verify all trace metadata is preserved
        Assert.NotNull(loaded.Metadata.GetEventId());
        Assert.NotNull(loaded.Metadata.GetBatchId());
        Assert.Equal(providedCorrelationId, loaded.Metadata.GetCorrelationId());
        Assert.Equal(providedCausationId, loaded.Metadata.GetCausationId());
    }
}

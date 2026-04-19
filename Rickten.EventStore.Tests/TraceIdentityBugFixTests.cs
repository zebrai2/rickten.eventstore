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

namespace Rickten.EventStore.Tests;

[Event("BugFixTest", "TestEvent", 1)]
public record BugFixTestEvent(string Name);

/// <summary>
/// Tests for trace identity bug fixes:
/// 1. Missing correlation IDs should be shared across batch
/// 2. Reaction propagation should preserve metadata source
/// 3. GetEventId should only return System-source EventId
/// </summary>
public class TraceIdentityBugFixTests
{
    private static readonly ITypeMetadataRegistry Registry = TestTypeMetadataRegistry.Create();

    private EventStoreDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new EventStoreDbContext(options);
    }

    private EntityFramework.EventStore CreateStore(string dbName) =>
        new EntityFramework.EventStore(CreateContext(dbName), Registry, new WireTypeSerializer(Registry));

    private StreamPointer MakePointer(string streamType, string streamId, long version) =>
        new StreamPointer(new StreamIdentifier(streamType, streamId), version);

    [Fact]
    public async Task Bug1_Missing_CorrelationIds_Are_Shared_Across_Batch()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("BugFixTest", "batch-test", 0);

        // Act: Append multiple events without providing CorrelationId
        var events = new[]
        {
            new AppendEvent(new BugFixTestEvent("Event1")),
            new AppendEvent(new BugFixTestEvent("Event2")),
            new AppendEvent(new BugFixTestEvent("Event3"))
        };

        await store.AppendAsync(pointer, events);

        // Assert: All events in the batch should share the same CorrelationId
        var loadedEvents = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(pointer.Stream))
            loadedEvents.Add(e);
        Assert.Equal(3, loadedEvents.Count);

        var correlationId1 = loadedEvents[0].Metadata.GetCorrelationId();
        var correlationId2 = loadedEvents[1].Metadata.GetCorrelationId();
        var correlationId3 = loadedEvents[2].Metadata.GetCorrelationId();

        Assert.NotNull(correlationId1);
        Assert.Equal(correlationId1, correlationId2);
        Assert.Equal(correlationId1, correlationId3);
    }

    [Fact]
    public async Task Bug2_Reaction_Propagation_Preserves_Metadata_Source()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("BugFixTest", "source-test", 0);

        // Act: Append event without CorrelationId (system will generate one with System source)
        await store.AppendAsync(pointer, new[] { new AppendEvent(new BugFixTestEvent("Trigger")) });

        var loadedEvents = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(pointer.Stream))
            loadedEvents.Add(e);
        var triggerEvent = loadedEvents.Single();

        // Get the system-generated CorrelationId value
        var triggerCorrelationId = triggerEvent.Metadata.GetCorrelationId();
        Assert.NotNull(triggerCorrelationId);

        var triggerCorrelationMetadata = triggerEvent.Metadata.GetMetadataWithSource(EventMetadataKeys.CorrelationId);
        Assert.NotNull(triggerCorrelationMetadata);
        Assert.Equal(EventMetadataSource.System, triggerCorrelationMetadata.Source);

        // Simulate reaction propagation - just pass the CorrelationId value
        var reactionMetadata = new AppendMetadata(EventMetadataKeys.CorrelationId, triggerCorrelationId);

        // Act: Append a new event with propagated metadata
        var reactionPointer = MakePointer("BugFixTest", "reaction-target", 0);
        await store.AppendAsync(
            reactionPointer,
            new[] { new AppendEvent(new BugFixTestEvent("Reaction"), new[] { reactionMetadata }) });

        // Assert: The reaction CorrelationId should be Client source (AppendMetadata is client-provided)
        // even though causation was System source
        var reactionEvents = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(reactionPointer.Stream))
            reactionEvents.Add(e);
        var reactionEvent = reactionEvents.Single();

        var propagatedCorrelationMetadata = reactionEvent.Metadata.GetMetadataWithSource(EventMetadataKeys.CorrelationId);
        Assert.NotNull(propagatedCorrelationMetadata);
        Assert.Equal(EventMetadataSource.Client, propagatedCorrelationMetadata.Source);

        // Verify the CorrelationId value is the same
        var propagatedCorrelationId = reactionEvent.Metadata.GetCorrelationId();
        Assert.Equal(triggerCorrelationId, propagatedCorrelationId);
    }

    [Fact]
    public async Task Bug3_GetEventId_Rejects_Client_Supplied_EventId()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("BugFixTest", "spoof-test", 0);
        var spoofedEventId = Guid.NewGuid();

        // Act: Try to append event with client-supplied EventId (attempted spoofing)
        var metadata = new AppendMetadata(EventMetadataKeys.EventId, spoofedEventId);
        await store.AppendAsync(
            pointer,
            new[] { new AppendEvent(new BugFixTestEvent("Test"), new[] { metadata }) });

        // Assert: GetEventId should only return the system-generated EventId, not the spoofed one
        var loadedEvents = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(pointer.Stream))
            loadedEvents.Add(e);
        var loadedEvent = loadedEvents.Single();

        var eventId = loadedEvent.Metadata.GetEventId();
        Assert.NotNull(eventId);
        Assert.NotEqual(spoofedEventId, eventId!.Value); // Should be different from spoofed value

        // Verify the system EventId exists and is different
        var systemEventId = loadedEvent.Metadata.GetSystemEventId();
        Assert.NotNull(systemEventId);
        Assert.NotEqual(spoofedEventId, systemEventId!.Value);
        Assert.Equal(eventId!.Value, systemEventId!.Value); // GetEventId == GetSystemEventId
    }

    [Fact]
    public async Task Bug3_GetSystemEventId_Ignores_Client_EventId()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("BugFixTest", "system-test", 0);
        var clientEventId = Guid.NewGuid();

        // Act: Append event with client-supplied EventId
        var metadata = new AppendMetadata(EventMetadataKeys.EventId, clientEventId);
        await store.AppendAsync(
            pointer,
            new[] { new AppendEvent(new BugFixTestEvent("Test"), new[] { metadata }) });

        // Assert: GetSystemEventId should ignore client metadata and only return System source
        var loadedEvents = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(pointer.Stream))
            loadedEvents.Add(e);
        var loadedEvent = loadedEvents.Single();

        var systemEventId = loadedEvent.Metadata.GetSystemEventId();
        Assert.NotNull(systemEventId);
        Assert.NotEqual(clientEventId, systemEventId!.Value);

        // Verify both client and system EventIds exist in metadata
        var allEventIds = loadedEvent.Metadata
            .Where(m => m.Key == EventMetadataKeys.EventId)
            .ToList();

        Assert.Equal(2, allEventIds.Count); // Client + System

        // Helper to extract Guid from metadata value (handles both Guid and JsonElement)
        static Guid GetGuidValue(object? value)
        {
            if (value is Guid g) return g;
            if (value is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String)
                return Guid.Parse(je.GetString()!);
            throw new InvalidOperationException($"Cannot extract Guid from {value?.GetType().Name ?? "null"}");
        }

        Assert.Contains(allEventIds, m => m.Source == EventMetadataSource.Client && GetGuidValue(m.Value) == clientEventId);
        Assert.Contains(allEventIds, m => m.Source == EventMetadataSource.System && GetGuidValue(m.Value) == systemEventId!.Value);
    }
}

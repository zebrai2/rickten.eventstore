using Xunit;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore.TypeMetadata;
using Rickten.Aggregator;
using System;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Tests for EventStoreSerializer with registry-backed wire name resolution for events.
/// </summary>
public class EventSerializerTests
{
    [Event("TestAggregate", "TestEvent", 1)]
    public record TestEvent(string Data, int Count);

    [Event("TestAggregate", "AnotherEvent", 1)]
    public record AnotherEvent(decimal Amount);

    [Event("DifferentAggregate", "SomeEvent", 2)]
    public record SomeEvent(bool Flag);

    public record UnregisteredEvent(string Data);

    [Fact]
    public void GetWireName_RegisteredEvent_ReturnsCorrectWireName()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new EventStoreSerializer(registry);
        var evt = new TestEvent("test", 42);

        var wireName = serializer.GetWireName(evt);

        Assert.Equal("TestAggregate.TestEvent.v1", wireName);
    }

    [Fact]
    public void GetWireName_EventWithDifferentVersion_IncludesVersion()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new EventStoreSerializer(registry);
        var evt = new SomeEvent(true);

        var wireName = serializer.GetWireName(evt);

        Assert.Equal("DifferentAggregate.SomeEvent.v2", wireName);
    }

    [Fact]
    public void Serialize_RegisteredEvent_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new EventStoreSerializer(registry);
        var evt = new TestEvent("test", 42);

        var json = serializer.Serialize(evt);

        Assert.NotNull(json);
        Assert.Contains("test", json);
        Assert.Contains("42", json);
    }

    [Fact]
    public void Deserialize_RegisteredEvent_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new EventStoreSerializer(registry);
        var json = """{"data":"test","count":42}""";

        var result = serializer.Deserialize(json, "TestAggregate.TestEvent.v1");

        Assert.NotNull(result);
        var evt = Assert.IsType<TestEvent>(result);
        Assert.Equal("test", evt.Data);
        Assert.Equal(42, evt.Count);
    }

    [Fact]
    public void RoundTrip_RegisteredEvent_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new EventStoreSerializer(registry);
        var original = new TestEvent("roundtrip", 99);

        var wireName = serializer.GetWireName(original);
        var json = serializer.Serialize(original);
        var result = serializer.Deserialize(json, wireName);

        Assert.NotNull(result);
        var deserialized = Assert.IsType<TestEvent>(result);
        Assert.Equal(original.Data, deserialized.Data);
        Assert.Equal(original.Count, deserialized.Count);
    }

    [Fact]
    public void RoundTrip_MultipleEventTypes_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new EventStoreSerializer(registry);

        var evt1 = new TestEvent("first", 1);
        var evt2 = new AnotherEvent(123.45m);
        var evt3 = new SomeEvent(true);

        var wireName1 = serializer.GetWireName(evt1);
        var json1 = serializer.Serialize(evt1);
        var result1 = serializer.Deserialize(json1, wireName1);

        var wireName2 = serializer.GetWireName(evt2);
        var json2 = serializer.Serialize(evt2);
        var result2 = serializer.Deserialize(json2, wireName2);

        var wireName3 = serializer.GetWireName(evt3);
        var json3 = serializer.Serialize(evt3);
        var result3 = serializer.Deserialize(json3, wireName3);

        Assert.Equal("TestAggregate.TestEvent.v1", wireName1);
        Assert.IsType<TestEvent>(result1);

        Assert.Equal("TestAggregate.AnotherEvent.v1", wireName2);
        Assert.IsType<AnotherEvent>(result2);

        Assert.Equal("DifferentAggregate.SomeEvent.v2", wireName3);
        Assert.IsType<SomeEvent>(result3);
    }

    [Fact]
    public void GetWireName_UnregisteredEvent_ThrowsException()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new EventStoreSerializer(registry);
        var evt = new UnregisteredEvent("unregistered");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.GetWireName(evt));

        Assert.Contains("not registered", ex.Message);
        Assert.Contains("UnregisteredEvent", ex.Message);
    }

    [Fact]
    public void Deserialize_UnregisteredWireName_ThrowsException()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new EventStoreSerializer(registry);
        var json = """{"data":"test"}""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.Deserialize(json, "Unknown.Event.v1"));

        Assert.Contains("Cannot resolve type", ex.Message);
        Assert.Contains("Unknown.Event.v1", ex.Message);
    }
}

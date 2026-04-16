using Xunit;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore.TypeMetadata;
using Rickten.Aggregator;
using System;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Tests for StateSerializer ensuring explicit registry-driven type resolution.
/// </summary>
public class StateSerializerTests
{
    [Aggregate("TestAggregate")]
    public record TestState(string Name, int Value);

    [Aggregate("AnotherAggregate")]
    public record AnotherState(decimal Amount);

    public record UnregisteredState(string Data);

    [Fact]
    public void Serialize_RegisteredState_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new StateSerializer(registry);
        var state = new TestState("test", 42);

        var json = serializer.Serialize(state);

        Assert.NotNull(json);
        Assert.Contains("test", json);
        Assert.Contains("42", json);
    }

    [Fact]
    public void GetTypeName_RegisteredState_ReturnsWireName()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new StateSerializer(registry);
        var state = new TestState("test", 42);

        var typeName = serializer.GetTypeName(state);

        Assert.Equal("TestAggregate.TestState", typeName);
    }

    [Fact]
    public void GetTypeName_UnregisteredState_ThrowsException()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new StateSerializer(registry);
        var state = new UnregisteredState("unregistered");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.GetTypeName(state));

        Assert.Contains("not registered", ex.Message);
        Assert.Contains("UnregisteredState", ex.Message);
        Assert.Contains("ITypeMetadataRegistry", ex.Message);
    }

    [Fact]
    public void Deserialize_RegisteredState_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new StateSerializer(registry);
        var json = """{"name":"test","value":42}""";

        var result = serializer.Deserialize(json, "TestAggregate.TestState");

        Assert.NotNull(result);
        var state = Assert.IsType<TestState>(result);
        Assert.Equal("test", state.Name);
        Assert.Equal(42, state.Value);
    }

    [Fact]
    public void Deserialize_UnregisteredTypeName_ThrowsException()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new StateSerializer(registry);
        var json = """{"data":"test"}""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.Deserialize(json, "UnknownAggregate.UnknownState"));

        Assert.Contains("Cannot resolve state type", ex.Message);
        Assert.Contains("UnknownAggregate.UnknownState", ex.Message);
        Assert.Contains("not registered", ex.Message);
        Assert.Contains("ITypeMetadataRegistry", ex.Message);
    }

    [Fact]
    public void Deserialize_FullNameInsteadOfWireName_ThrowsException()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new StateSerializer(registry);
        var json = """{"name":"test","value":42}""";

        // Even though TestState is registered, using FullName instead of WireName should fail
        var fullName = typeof(TestState).FullName!;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.Deserialize(json, fullName));

        Assert.Contains("Cannot resolve state type", ex.Message);
        Assert.Contains(fullName, ex.Message);
        Assert.Contains("not registered", ex.Message);
    }

    [Fact]
    public void RoundTrip_RegisteredState_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new StateSerializer(registry);
        var original = new TestState("roundtrip", 99);

        var typeName = serializer.GetTypeName(original);
        var json = serializer.Serialize(original);
        var result = serializer.Deserialize(json, typeName);

        Assert.NotNull(result);
        var deserialized = Assert.IsType<TestState>(result);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public void RoundTrip_MultipleRegisteredStates_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new StateSerializer(registry);

        var state1 = new TestState("first", 1);
        var state2 = new AnotherState(123.45m);

        var typeName1 = serializer.GetTypeName(state1);
        var json1 = serializer.Serialize(state1);
        var result1 = serializer.Deserialize(json1, typeName1);

        var typeName2 = serializer.GetTypeName(state2);
        var json2 = serializer.Serialize(state2);
        var result2 = serializer.Deserialize(json2, typeName2);

        Assert.Equal("TestAggregate.TestState", typeName1);
        Assert.IsType<TestState>(result1);

        Assert.Equal("AnotherAggregate.AnotherState", typeName2);
        Assert.IsType<AnotherState>(result2);
    }

    [Fact]
    public void Constructor_NullRegistry_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StateSerializer(null!));

        Assert.Equal("registry", ex.ParamName);
    }

    [Fact]
    public void Deserialize_NullJson_Throws()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new StateSerializer(registry);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.Deserialize("null", "TestAggregate.TestState"));

        Assert.Contains("Failed to deserialize", ex.Message);
        Assert.Contains("TestAggregate.TestState", ex.Message);
    }
}

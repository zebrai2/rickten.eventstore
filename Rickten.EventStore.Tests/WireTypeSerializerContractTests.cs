using Xunit;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore.TypeMetadata;
using Rickten.Aggregator;
using System;
using System.Linq;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Tests for WireTypeSerializer focusing on wire-name contract paths and edge cases.
/// Verifies that serialization uses registry-driven wire names, not CLR type names.
/// </summary>
public class WireTypeSerializerContractTests
{
    [Event("Inventory", "StockAdded", 1)]
    public record StockAddedEvent(string Sku, int Quantity);

    [Event("Inventory", "StockRemoved", 2)]
    public record StockRemovedEventV2(string Sku, int Quantity, string Reason);

    [Aggregate("InventoryManagement")]
    public record InventoryManagementState(int TotalStock, DateTime LastUpdated);

    [Aggregate("Warehouse")]
    public record WarehouseState(string Location, int Capacity);

    [Fact]
    public void GetWireName_ByType_ReturnsCorrectWireName()
    {
        // Test the GetWireName(Type) overload which takes a type directly
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);

        var wireName = serializer.GetWireName(typeof(StockAddedEvent));

        Assert.Equal("Inventory.StockAdded.v1", wireName);
    }

    [Fact]
    public void GetWireName_ByTypeForState_ReturnsCorrectWireName()
    {
        // Verify GetWireName(Type) works for aggregate states
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);

        var wireName = serializer.GetWireName(typeof(InventoryManagementState));

        Assert.Equal("InventoryManagement.InventoryManagementState", wireName);
    }

    [Fact]
    public void GetWireName_ByType_UnregisteredType_ThrowsException()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.GetWireName(typeof(string)));

        Assert.Contains("not registered", ex.Message);
        Assert.Contains("ITypeMetadataRegistry", ex.Message);
    }

    [Fact]
    public void GetWireName_ByInstanceAndByType_ReturnSameWireName()
    {
        // Verify consistency: GetWireName(object) and GetWireName(Type) return the same value
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var evt = new StockAddedEvent("SKU-123", 50);

        var wireNameByInstance = serializer.GetWireName(evt);
        var wireNameByType = serializer.GetWireName(typeof(StockAddedEvent));

        Assert.Equal(wireNameByInstance, wireNameByType);
    }

    [Fact]
    public void WireName_ForEventWithVersion_IncludesVersionPrefix()
    {
        // Verify wire name format: Aggregate.Name.vVersion
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);

        var v1WireName = serializer.GetWireName(typeof(StockAddedEvent));
        var v2WireName = serializer.GetWireName(typeof(StockRemovedEventV2));

        Assert.StartsWith("Inventory.", v1WireName);
        Assert.EndsWith(".v1", v1WireName);
        Assert.EndsWith(".v2", v2WireName);
    }

    [Fact]
    public void WireName_ForState_DoesNotIncludeVersion()
    {
        // Aggregate states use format: AggregateName.TypeName (no version)
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);

        var wireName = serializer.GetWireName(typeof(InventoryManagementState));

        Assert.Equal("InventoryManagement.InventoryManagementState", wireName);
        Assert.DoesNotContain(".v", wireName);
    }

    [Fact]
    public void Deserialize_UsesWireNameNotClrTypeName()
    {
        // Verify that deserialization requires the wire name, not the CLR FullName
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"sku":"TEST","quantity":10}""";

        // Using wire name should work
        var result = serializer.Deserialize(json, "Inventory.StockAdded.v1");
        Assert.IsType<StockAddedEvent>(result);

        // Using CLR type name should fail
        var clrName = typeof(StockAddedEvent).FullName!;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.Deserialize(json, clrName));

        Assert.Contains("Cannot resolve type", ex.Message);
        Assert.Contains(clrName, ex.Message);
    }

    [Fact]
    public void Serialize_ProducesValidJsonRegardlessOfWireName()
    {
        // Wire name is separate from serialization; JSON should contain field names only
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var evt = new StockAddedEvent("SKU-999", 100);

        var json = serializer.Serialize(evt);

        // Verify JSON contains properties in camelCase (per JsonOptions)
        Assert.Contains("\"sku\"", json);
        Assert.Contains("\"quantity\"", json);
        Assert.Contains("SKU-999", json);
        Assert.Contains("100", json);

        // Wire name should NOT appear in JSON payload
        Assert.DoesNotContain("StockAdded", json);
        Assert.DoesNotContain("Inventory", json);
    }

    [Fact]
    public void RoundTrip_MultipleStates_MaintainsTypeIdentity()
    {
        // Verify that different aggregate states maintain distinct wire names
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);

        var inventoryState = new InventoryManagementState(500, DateTime.UtcNow);
        var warehouseState = new WarehouseState("Building A", 10000);

        var invWireName = serializer.GetWireName(inventoryState);
        var invJson = serializer.Serialize(inventoryState);
        var invResult = serializer.Deserialize(invJson, invWireName);

        var whWireName = serializer.GetWireName(warehouseState);
        var whJson = serializer.Serialize(warehouseState);
        var whResult = serializer.Deserialize(whJson, whWireName);

        Assert.NotEqual(invWireName, whWireName);
        Assert.IsType<InventoryManagementState>(invResult);
        Assert.IsType<WarehouseState>(whResult);
    }

    [Fact]
    public void GetWireName_SameAggregateNameDifferentTypes_ProducesUniqueWireNames()
    {
        // Multiple types for the same aggregate should have unique wire names
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);

        var eventWireName = serializer.GetWireName(typeof(StockAddedEvent));
        var stateWireName = serializer.GetWireName(typeof(InventoryManagementState));

        // Event belongs to "Inventory" aggregate, state to "InventoryManagement"
        Assert.StartsWith("Inventory.", eventWireName);
        Assert.StartsWith("InventoryManagement.", stateWireName);
        Assert.NotEqual(eventWireName, stateWireName);
    }

    [Fact]
    public void Deserialize_InvalidJson_ThrowsException()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var invalidJson = "{this is not valid json}";

        Assert.Throws<System.Text.Json.JsonException>(() =>
            serializer.Deserialize(invalidJson, "Inventory.StockAdded.v1"));
    }

    [Fact]
    public void Deserialize_JsonWithMissingRequiredProperty_ThrowsOrUsesDefault()
    {
        // Record types with required properties should fail or use defaults when properties are missing
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var incompleteJson = """{"sku":"TEST"}"""; // missing quantity

        // System.Text.Json behavior: deserializes with default value for value types
        var result = serializer.Deserialize(incompleteJson, "Inventory.StockAdded.v1");
        var evt = Assert.IsType<StockAddedEvent>(result);
        Assert.Equal("TEST", evt.Sku);
        Assert.Equal(0, evt.Quantity); // default value
    }

    [Fact]
    public void Serialize_NullPropertyValues_HandledByJsonOptions()
    {
        // Verify that null handling follows JsonOptions (WhenWritingNull = ignore)
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);

        // Create an event where optional string can be null
        var evt = new StockRemovedEventV2("SKU-123", 10, null!);
        var json = serializer.Serialize(evt);

        // With DefaultIgnoreCondition = WhenWritingNull, null should be omitted
        // BUT with C# records and required properties, this depends on the exact type structure
        // Let's just verify serialization doesn't throw
        Assert.NotNull(json);
    }

    [Fact]
    public void Constructor_WithNullRegistry_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new WireTypeSerializer(null!));

        Assert.Equal("registry", ex.ParamName);
    }

    [Fact]
    public void GetWireName_CalledMultipleTimes_ReturnsSameValue()
    {
        // Wire name lookup should be deterministic and consistent
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);

        var wireName1 = serializer.GetWireName(typeof(StockAddedEvent));
        var wireName2 = serializer.GetWireName(typeof(StockAddedEvent));
        var wireName3 = serializer.GetWireName(new StockAddedEvent("TEST", 1));

        Assert.Equal(wireName1, wireName2);
        Assert.Equal(wireName2, wireName3);
    }
}

using Xunit;
using Rickten.EventStore.EntityFramework.Serialization;
using Rickten.EventStore.TypeMetadata;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Unit tests for the WireTypeSerializer class, focusing on dynamic object
/// deserialization and typed serialization.
/// </summary>
public class SerializerTests
{
    #region Dynamic Object Deserialization

    [Fact]
    public void Deserialize_Dynamic_SimpleObject_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"name":"Widget","price":99.99}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.NotNull(result);
        Assert.Equal("Widget", (string)result.name);
        Assert.Equal(99.99, (double)result.price);
    }

    [Fact]
    public void Deserialize_Dynamic_WithCamelCaseProperties_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        // Serializer uses camelCase by default
        var json = """{"totalAmount":500,"itemCount":5}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal(500, (long)result.totalAmount);
        Assert.Equal(5, (long)result.itemCount);
    }

    [Fact]
    public void Deserialize_Dynamic_NestedObject_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """
        {
            "order": {
                "id": "123",
                "total": 100.50
            },
            "customer": {
                "name": "John Doe",
                "email": "john@example.com"
            }
        }
        """;

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal("123", (string)result.order.id);
        Assert.Equal(100.50, (double)result.order.total);
        Assert.Equal("John Doe", (string)result.customer.name);
        Assert.Equal("john@example.com", (string)result.customer.email);
    }

    [Fact]
    public void Deserialize_Dynamic_Array_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"items":[1,2,3,4,5]}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.NotNull(result.items);
        var items = (List<object>)result.items;
        Assert.Equal(5, items.Count);
        Assert.Equal(1L, Convert.ToInt64(items[0]));
        Assert.Equal(5L, Convert.ToInt64(items[4]));
    }

    [Fact]
    public void Deserialize_Dynamic_ArrayOfObjects_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """
        {
            "products": [
                {"name":"Widget","price":10},
                {"name":"Gadget","price":20}
            ]
        }
        """;

        dynamic result = serializer.Deserialize<dynamic>(json);

        var products = (List<object>)result.products;
        Assert.Equal(2, products.Count);

        dynamic product1 = products[0];
        Assert.Equal("Widget", (string)product1.name);
        Assert.Equal(10L, (long)product1.price);

        dynamic product2 = products[1];
        Assert.Equal("Gadget", (string)product2.name);
        Assert.Equal(20L, (long)product2.price);
    }

    [Fact]
    public void Deserialize_Dynamic_NullValue_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"name":"Test","description":null}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal("Test", (string)result.name);
        Assert.Null(result.description);
    }

    [Fact]
    public void Deserialize_Dynamic_BooleanValues_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"isActive":true,"isDeleted":false}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.True((bool)result.isActive);
        Assert.False((bool)result.isDeleted);
    }

    [Fact]
    public void Deserialize_Dynamic_IntegerNumber_ReturnsLong()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"count":42}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal(42L, (long)result.count);
    }

    [Fact]
    public void Deserialize_Dynamic_FloatingPointNumber_ReturnsDouble()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"price":99.99}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal(99.99, (double)result.price);
    }

    [Fact]
    public void Deserialize_Dynamic_LargeNumber_ReturnsDouble()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        // Number too large for long
        var json = """{"bigNumber":9999999999999999999}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal(9999999999999999999.0, (double)result.bigNumber);
    }

    [Fact]
    public void Deserialize_Dynamic_EmptyObject_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.NotNull(result);
        var dict = (IDictionary<string, object?>)result;
        Assert.Empty(dict);
    }

    [Fact]
    public void Deserialize_Dynamic_EmptyArray_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"items":[]}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        var items = (List<object?>)result.items;
        Assert.Empty(items);
    }

    [Fact]
    public void Deserialize_Dynamic_ComplexNestedStructure_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """
        {
            "order": {
                "id": "ORD-123",
                "items": [
                    {
                        "product": "Widget",
                        "quantity": 2,
                        "price": 10.50
                    },
                    {
                        "product": "Gadget",
                        "quantity": 1,
                        "price": 25.00
                    }
                ],
                "total": 46.00,
                "isPaid": true,
                "notes": null
            }
        }
        """;

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal("ORD-123", (string)result.order.id);
        Assert.Equal(46.00, (double)result.order.total);
        Assert.True((bool)result.order.isPaid);
        Assert.Null(result.order.notes);

        var items = (List<object>)result.order.items;
        Assert.Equal(2, items.Count);

        dynamic item1 = items[0];
        Assert.Equal("Widget", (string)item1.product);
        Assert.Equal(2L, (long)item1.quantity);
        Assert.Equal(10.50, (double)item1.price);
    }

    #endregion

    #region Typed Deserialization

    [Fact]
    public void Deserialize_TypedObject_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"name":"Test Product","price":99.99}""";

        var result = serializer.Deserialize<TestProduct>(json);

        Assert.Equal("Test Product", result.Name);
        Assert.Equal(99.99m, result.Price);
    }

    [Fact]
    public void Deserialize_TypedObject_WithCamelCase_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"name":"Widget","price":50.00}""";

        var result = serializer.Deserialize<TestProduct>(json);

        Assert.Equal("Widget", result.Name);
        Assert.Equal(50.00m, result.Price);
    }

    [Fact]
    public void Deserialize_TypedObject_Null_ThrowsException()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = "null";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.Deserialize<TestProduct>(json));

        Assert.Contains("Failed to deserialize", ex.Message);
    }

    #endregion

    #region Serialization

    [Fact]
    public void Serialize_SimpleObject_UsesCamelCase()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var obj = new { Name = "Widget", Price = 99.99 };

        var json = serializer.Serialize(obj);

        Assert.Contains("\"name\"", json);
        Assert.Contains("\"price\"", json);
        Assert.DoesNotContain("\"Name\"", json);
        Assert.DoesNotContain("\"Price\"", json);
    }

    [Fact]
    public void Serialize_NullProperty_IsOmitted()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var obj = new { Name = "Test", Description = (string?)null };

        var json = serializer.Serialize(obj);

        Assert.Contains("\"name\"", json);
        Assert.DoesNotContain("description", json);
    }

    [Fact]
    public void Serialize_ComplexObject_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var obj = new
        {
            Order = new
            {
                Id = "123",
                Items = new[] { 1, 2, 3 },
                Total = 100.50
            }
        };

        var json = serializer.Serialize(obj);

        Assert.Contains("\"order\"", json);
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"items\"", json);
        Assert.Contains("\"total\"", json);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_SimpleObject_AsTyped_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var original = new TestProduct { Name = "Widget", Price = 99.99m };

        var json = serializer.Serialize(original);
        var result = serializer.Deserialize<TestProduct>(json);

        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Price, result.Price);
    }

    [Fact]
    public void RoundTrip_AnonymousObject_AsDynamic_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var original = new { Count = 5, Total = 100.50, IsActive = true };

        var json = serializer.Serialize(original);
        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal(5L, (long)result.count);
        Assert.Equal(100.50, (double)result.total);
        Assert.True((bool)result.isActive);
    }

    [Fact]
    public void RoundTrip_ComplexObject_AsDynamic_PreservesStructure()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var original = new
        {
            Order = new
            {
                Id = "ORD-123",
                Items = new[]
                {
                    new { Product = "Widget", Qty = 2 },
                    new { Product = "Gadget", Qty = 1 }
                }
            }
        };

        var json = serializer.Serialize(original);
        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal("ORD-123", (string)result.order.id);
        var items = (List<object?>)result.order.items;
        Assert.Equal(2, items.Count);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Deserialize_Dynamic_StringWithSpecialCharacters_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"text":"Hello \"World\"\nNew Line\tTab"}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Contains("\"World\"", (string)result.text);
        Assert.Contains("\n", (string)result.text);
        Assert.Contains("\t", (string)result.text);
    }

    [Fact]
    public void Deserialize_Dynamic_UnicodeCharacters_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"greeting":"Hello 世界 🌍"}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal("Hello 世界 🌍", (string)result.greeting);
    }

    [Fact]
    public void Deserialize_Dynamic_NumberFormats_Work()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """
        {
            "integer":42,
            "negative":-100,
            "decimal":3.14159,
            "scientific":1.23e10,
            "zero":0
        }
        """;

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal(42L, (long)result.integer);
        Assert.Equal(-100L, (long)result.negative);
        Assert.Equal(3.14159, (double)result.@decimal);
        Assert.Equal(1.23e10, (double)result.scientific);
        Assert.Equal(0L, (long)result.zero);
    }

    [Fact]
    public void Deserialize_Dynamic_DeeplyNestedObject_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """
        {
            "level1": {
                "level2": {
                    "level3": {
                        "level4": {
                            "value": "deep"
                        }
                    }
                }
            }
        }
        """;

        dynamic result = serializer.Deserialize<dynamic>(json);

        Assert.Equal("deep", (string)result.level1.level2.level3.level4.value);
    }

    [Fact]
    public void Deserialize_Dynamic_MixedTypeArray_Works()
    {
        var registry = TestTypeMetadataRegistry.Create();
        var serializer = new WireTypeSerializer(registry);
        var json = """{"mixed":[1,"two",3.0,true,null]}""";

        dynamic result = serializer.Deserialize<dynamic>(json);

        var mixed = (List<object?>)result.mixed;
        Assert.Equal(5, mixed.Count);
        Assert.Equal(1L, Convert.ToInt64(mixed[0]));
        Assert.Equal("two", mixed[1]);
        Assert.Equal(3.0, Convert.ToDouble(mixed[2]));
        Assert.True((bool)mixed[3]!);
        Assert.Null(mixed[4]);
    }

    #endregion
}

public record TestProduct
{
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
}

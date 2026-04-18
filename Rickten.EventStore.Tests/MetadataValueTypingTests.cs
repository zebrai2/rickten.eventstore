using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Rickten.EventStore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.EntityFramework.Serialization;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Tests for metadata value typing behavior after round-trip through storage.
/// EventMetadata.Value is object?, but after JSON serialization/deserialization,
/// values materialize as JsonElement rather than their original CLR types.
/// </summary>
public class MetadataValueTypingTests
{
    private static EntityFramework.EventStore CreateStore(string dbName)
    {
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var context = new EventStoreDbContext(options);
        var registry = TestTypeMetadataRegistry.Create();
        return new EntityFramework.EventStore(context, registry, new WireTypeSerializer(registry));
    }

    private static StreamPointer MakePointer(string type, string id, long version)
        => new StreamPointer(new StreamIdentifier(type, id), version);

    [Fact]
    public async Task Metadata_StringValue_RoundTrips()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "1", 0);

        var stringValue = "test-value";
        var appendEvents = new List<AppendEvent>
        {
            new AppendEvent(
                new OrderCreatedEvent(100),
                new[] { new AppendMetadata("StringKey", stringValue) })
        };

        await store.AppendAsync(pointer, appendEvents);

        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(MakePointer("Order", "1", 0)))
            loaded.Add(e);

        var metadata = loaded[0].Metadata.First(m => m.Key == "StringKey");

        // After round-trip through JSON, the value is a JsonElement
        Assert.IsType<JsonElement>(metadata.Value);

        // String values can be extracted from JsonElement
        var jsonElement = (JsonElement)metadata.Value!;
        Assert.Equal(JsonValueKind.String, jsonElement.ValueKind);
        Assert.Equal(stringValue, jsonElement.GetString());
    }

    [Fact]
    public async Task Metadata_DateTimeValue_BecomesJsonElement()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "2", 0);

        var dateTimeValue = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var appendEvents = new List<AppendEvent>
        {
            new AppendEvent(
                new OrderCreatedEvent(100),
                new[] { new AppendMetadata("CreatedDate", dateTimeValue) })
        };

        await store.AppendAsync(pointer, appendEvents);

        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(MakePointer("Order", "2", 0)))
            loaded.Add(e);

        var metadata = loaded[0].Metadata.First(m => m.Key == "CreatedDate");

        // After round-trip, DateTime becomes JsonElement
        Assert.IsType<JsonElement>(metadata.Value);

        // DateTime is serialized as ISO 8601 string
        var jsonElement = (JsonElement)metadata.Value!;
        Assert.Equal(JsonValueKind.String, jsonElement.ValueKind);

        // Can parse back to DateTime
        var parsedDateTime = jsonElement.GetDateTime();
        Assert.Equal(dateTimeValue, parsedDateTime);
    }

    [Fact]
    public async Task Metadata_GuidValue_BecomesJsonElement()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "3", 0);

        var guidValue = Guid.NewGuid();
        var appendEvents = new List<AppendEvent>
        {
            new AppendEvent(
                new OrderCreatedEvent(100),
                new[] { new AppendMetadata("CorrelationId", guidValue) })
        };

        await store.AppendAsync(pointer, appendEvents);

        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(MakePointer("Order", "3", 0)))
            loaded.Add(e);

        var metadata = loaded[0].Metadata.First(m => m.Key == "CorrelationId");

        // After round-trip, Guid becomes JsonElement
        Assert.IsType<JsonElement>(metadata.Value);

        // Guid is serialized as string
        var jsonElement = (JsonElement)metadata.Value!;
        Assert.Equal(JsonValueKind.String, jsonElement.ValueKind);

        // Can parse back to Guid
        var parsedGuid = jsonElement.GetGuid();
        Assert.Equal(guidValue, parsedGuid);
    }

    [Fact]
    public async Task Metadata_NumericValues_BecomeJsonElement()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "4", 0);

        var appendEvents = new List<AppendEvent>
        {
            new AppendEvent(
                new OrderCreatedEvent(100),
                new[]
                {
                    new AppendMetadata("IntValue", 42),
                    new AppendMetadata("LongValue", 1234567890L),
                    new AppendMetadata("DoubleValue", 3.14159),
                    new AppendMetadata("DecimalValue", 99.99m)
                })
        };

        await store.AppendAsync(pointer, appendEvents);

        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(MakePointer("Order", "4", 0)))
            loaded.Add(e);

        var metadata = loaded[0].Metadata;

        // Int
        var intMeta = metadata.First(m => m.Key == "IntValue");
        Assert.IsType<JsonElement>(intMeta.Value);
        var intElement = (JsonElement)intMeta.Value!;
        Assert.Equal(JsonValueKind.Number, intElement.ValueKind);
        Assert.Equal(42, intElement.GetInt32());

        // Long
        var longMeta = metadata.First(m => m.Key == "LongValue");
        Assert.IsType<JsonElement>(longMeta.Value);
        var longElement = (JsonElement)longMeta.Value!;
        Assert.Equal(JsonValueKind.Number, longElement.ValueKind);
        Assert.Equal(1234567890L, longElement.GetInt64());

        // Double
        var doubleMeta = metadata.First(m => m.Key == "DoubleValue");
        Assert.IsType<JsonElement>(doubleMeta.Value);
        var doubleElement = (JsonElement)doubleMeta.Value!;
        Assert.Equal(JsonValueKind.Number, doubleElement.ValueKind);
        Assert.Equal(3.14159, doubleElement.GetDouble(), precision: 5);

        // Decimal
        var decimalMeta = metadata.First(m => m.Key == "DecimalValue");
        Assert.IsType<JsonElement>(decimalMeta.Value);
        var decimalElement = (JsonElement)decimalMeta.Value!;
        Assert.Equal(JsonValueKind.Number, decimalElement.ValueKind);
        Assert.Equal(99.99m, decimalElement.GetDecimal());
    }

    [Fact]
    public async Task Metadata_BooleanValue_BecomesJsonElement()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "5", 0);

        var appendEvents = new List<AppendEvent>
        {
            new AppendEvent(
                new OrderCreatedEvent(100),
                new[]
                {
                    new AppendMetadata("IsActive", true),
                    new AppendMetadata("IsDeleted", false)
                })
        };

        await store.AppendAsync(pointer, appendEvents);

        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(MakePointer("Order", "5", 0)))
            loaded.Add(e);

        var metadata = loaded[0].Metadata;

        var trueMeta = metadata.First(m => m.Key == "IsActive");
        Assert.IsType<JsonElement>(trueMeta.Value);
        var trueElement = (JsonElement)trueMeta.Value!;
        Assert.Equal(JsonValueKind.True, trueElement.ValueKind);
        Assert.True(trueElement.GetBoolean());

        var falseMeta = metadata.First(m => m.Key == "IsDeleted");
        Assert.IsType<JsonElement>(falseMeta.Value);
        var falseElement = (JsonElement)falseMeta.Value!;
        Assert.Equal(JsonValueKind.False, falseElement.ValueKind);
        Assert.False(falseElement.GetBoolean());
    }

    [Fact]
    public async Task Metadata_NullValue_RoundTrips()
    {
        var dbName = Guid.NewGuid().ToString();
        var store = CreateStore(dbName);
        var pointer = MakePointer("Order", "6", 0);

        var appendEvents = new List<AppendEvent>
        {
            new AppendEvent(
                new OrderCreatedEvent(100),
                new[] { new AppendMetadata("NullableKey", null) })
        };

        await store.AppendAsync(pointer, appendEvents);

        var loaded = new List<StreamEvent>();
        await foreach (var e in store.LoadAsync(MakePointer("Order", "6", 0)))
            loaded.Add(e);

        var metadata = loaded[0].Metadata.First(m => m.Key == "NullableKey");

        // Null values remain null (they don't become JsonElement)
        Assert.Null(metadata.Value);
    }

    [Fact]
    public void Metadata_InvalidCast_ThrowsException()
    {
        // This test documents that casting to the original type DOES NOT WORK
        var dateTimeValue = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var metadata = new EventMetadata("System", "Timestamp", dateTimeValue);

        // Before serialization, the value is the original type
        Assert.IsType<DateTime>(metadata.Value);

        // After round-trip (simulated), the cast would fail:
        var serialized = JsonSerializer.Serialize(new[] { metadata });
        var deserialized = JsonSerializer.Deserialize<EventMetadata[]>(serialized);

        var roundTripped = deserialized![0];

        // This would throw InvalidCastException or return null
        Assert.Throws<InvalidCastException>(() =>
        {
            var _ = (DateTime)roundTripped.Value!;
        });

        // Even "as" cast returns null instead of the value
        var asDateTime = roundTripped.Value as DateTime?;
        Assert.Null(asDateTime);

        // The correct approach is to use JsonElement
        Assert.IsType<JsonElement>(roundTripped.Value);
    }
}

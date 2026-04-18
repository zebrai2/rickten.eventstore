using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Rickten.EventStore;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Tests for EventMetadataExtensions helper methods.
/// </summary>
public class EventMetadataExtensionsTests
{
    [Fact]
    public void GetString_WithStringValue_ReturnsValue()
    {
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "UserId", "user-123")
        };

        var result = metadata.GetString("UserId");
        Assert.Equal("user-123", result);
    }

    [Fact]
    public void GetString_WithJsonElement_ReturnsValue()
    {
        var jsonElement = JsonSerializer.Deserialize<JsonElement>("\"test-value\"");
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "Key", jsonElement)
        };

        var result = metadata.GetString("Key");
        Assert.Equal("test-value", result);
    }

    [Fact]
    public void GetString_WithNullValue_ReturnsNull()
    {
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "Key", null)
        };

        var result = metadata.GetString("Key");
        Assert.Null(result);
    }

    [Fact]
    public void GetString_WithMissingKey_ReturnsNull()
    {
        var metadata = new List<EventMetadata>();
        var result = metadata.GetString("MissingKey");
        Assert.Null(result);
    }

    [Fact]
    public void GetDateTime_WithDateTimeValue_ReturnsValue()
    {
        var dateTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("System", "Timestamp", dateTime)
        };

        var result = metadata.GetDateTime("Timestamp");
        Assert.Equal(dateTime, result);
    }

    [Fact]
    public void GetDateTime_WithJsonElement_ReturnsValue()
    {
        var dateTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var json = JsonSerializer.Serialize(dateTime);
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("System", "Timestamp", jsonElement)
        };

        var result = metadata.GetDateTime("Timestamp");
        Assert.NotNull(result);
        Assert.Equal(dateTime, result.Value);
    }

    [Fact]
    public void GetDateTime_WithNullValue_ReturnsNull()
    {
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("System", "Timestamp", null)
        };

        var result = metadata.GetDateTime("Timestamp");
        Assert.Null(result);
    }

    [Fact]
    public void GetGuid_WithGuidValue_ReturnsValue()
    {
        var guid = Guid.NewGuid();
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "CorrelationId", guid)
        };

        var result = metadata.GetGuid("CorrelationId");
        Assert.Equal(guid, result);
    }

    [Fact]
    public void GetGuid_WithJsonElement_ReturnsValue()
    {
        var guid = Guid.NewGuid();
        var json = JsonSerializer.Serialize(guid);
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "CorrelationId", jsonElement)
        };

        var result = metadata.GetGuid("CorrelationId");
        Assert.Equal(guid, result);
    }

    [Fact]
    public void GetInt32_WithIntValue_ReturnsValue()
    {
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "Count", 42)
        };

        var result = metadata.GetInt32("Count");
        Assert.Equal(42, result);
    }

    [Fact]
    public void GetInt32_WithJsonElement_ReturnsValue()
    {
        var json = JsonSerializer.Serialize(42);
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "Count", jsonElement)
        };

        var result = metadata.GetInt32("Count");
        Assert.Equal(42, result);
    }

    [Fact]
    public void GetInt64_WithLongValue_ReturnsValue()
    {
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "BigNumber", 1234567890L)
        };

        var result = metadata.GetInt64("BigNumber");
        Assert.Equal(1234567890L, result);
    }

    [Fact]
    public void GetInt64_WithJsonElement_ReturnsValue()
    {
        var json = JsonSerializer.Serialize(1234567890L);
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "BigNumber", jsonElement)
        };

        var result = metadata.GetInt64("BigNumber");
        Assert.Equal(1234567890L, result);
    }

    [Fact]
    public void GetDecimal_WithDecimalValue_ReturnsValue()
    {
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "Price", 99.99m)
        };

        var result = metadata.GetDecimal("Price");
        Assert.Equal(99.99m, result);
    }

    [Fact]
    public void GetDecimal_WithJsonElement_ReturnsValue()
    {
        var json = JsonSerializer.Serialize(99.99m);
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "Price", jsonElement)
        };

        var result = metadata.GetDecimal("Price");
        Assert.Equal(99.99m, result);
    }

    [Fact]
    public void GetDouble_WithDoubleValue_ReturnsValue()
    {
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "Pi", 3.14159)
        };

        var result = metadata.GetDouble("Pi");
        Assert.Equal(3.14159, result);
    }

    [Fact]
    public void GetDouble_WithJsonElement_ReturnsValue()
    {
        var json = JsonSerializer.Serialize(3.14159);
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "Pi", jsonElement)
        };

        var result = metadata.GetDouble("Pi");
        Assert.Equal(3.14159, result);
    }

    [Fact]
    public void GetBoolean_WithBoolValue_ReturnsValue()
    {
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "IsActive", true),
            new EventMetadata("Client", "IsDeleted", false)
        };

        Assert.True(metadata.GetBoolean("IsActive"));
        Assert.False(metadata.GetBoolean("IsDeleted"));
    }

    [Fact]
    public void GetBoolean_WithJsonElement_ReturnsValue()
    {
        var jsonTrue = JsonSerializer.Deserialize<JsonElement>("true");
        var jsonFalse = JsonSerializer.Deserialize<JsonElement>("false");
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "IsActive", jsonTrue),
            new EventMetadata("Client", "IsDeleted", jsonFalse)
        };

        Assert.True(metadata.GetBoolean("IsActive"));
        Assert.False(metadata.GetBoolean("IsDeleted"));
    }

    [Fact]
    public void GetBoolean_WithNullValue_ReturnsNull()
    {
        var metadata = new List<EventMetadata>
        {
            new EventMetadata("Client", "IsActive", null)
        };

        var result = metadata.GetBoolean("IsActive");
        Assert.Null(result);
    }
}

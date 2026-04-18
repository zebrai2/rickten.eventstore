using System.Text.Json;

namespace Rickten.EventStore;

/// <summary>
/// Extension methods for working with EventMetadata values.
/// EventMetadata.Value is object?, but after round-trip through storage,
/// non-null values materialize as JsonElement rather than their original CLR types.
/// These helpers provide safe, typed access to common metadata value types.
/// </summary>
public static class EventMetadataExtensions
{
    /// <summary>
    /// Safely gets a string value from metadata.
    /// Returns null if the metadata is not found or the value is null.
    /// </summary>
    public static string? GetString(this IReadOnlyList<EventMetadata> metadata, string key)
    {
        var meta = metadata.FirstOrDefault(m => m.Key == key);
        if (meta?.Value is null)
            return null;

        if (meta.Value is string str)
            return str;

        if (meta.Value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            return jsonElement.GetString();

        return meta.Value.ToString();
    }

    /// <summary>
    /// Safely gets a DateTime value from metadata.
    /// Returns null if the metadata is not found, the value is null, or cannot be parsed.
    /// </summary>
    public static DateTime? GetDateTime(this IReadOnlyList<EventMetadata> metadata, string key)
    {
        var meta = metadata.FirstOrDefault(m => m.Key == key);
        if (meta?.Value is null)
            return null;

        if (meta.Value is DateTime dt)
            return dt;

        if (meta.Value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                return jsonElement.TryGetDateTime(out var parsed) ? parsed : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Safely gets a Guid value from metadata.
    /// Returns null if the metadata is not found, the value is null, or cannot be parsed.
    /// </summary>
    public static Guid? GetGuid(this IReadOnlyList<EventMetadata> metadata, string key)
    {
        var meta = metadata.FirstOrDefault(m => m.Key == key);
        if (meta?.Value is null)
            return null;

        if (meta.Value is Guid guid)
            return guid;

        if (meta.Value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                return jsonElement.TryGetGuid(out var parsed) ? parsed : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Safely gets an int value from metadata.
    /// Returns null if the metadata is not found, the value is null, or cannot be parsed.
    /// </summary>
    public static int? GetInt32(this IReadOnlyList<EventMetadata> metadata, string key)
    {
        var meta = metadata.FirstOrDefault(m => m.Key == key);
        if (meta?.Value is null)
            return null;

        if (meta.Value is int i)
            return i;

        if (meta.Value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
            {
                return jsonElement.TryGetInt32(out var parsed) ? parsed : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Safely gets a long value from metadata.
    /// Returns null if the metadata is not found, the value is null, or cannot be parsed.
    /// </summary>
    public static long? GetInt64(this IReadOnlyList<EventMetadata> metadata, string key)
    {
        var meta = metadata.FirstOrDefault(m => m.Key == key);
        if (meta?.Value is null)
            return null;

        if (meta.Value is long l)
            return l;

        if (meta.Value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
            {
                return jsonElement.TryGetInt64(out var parsed) ? parsed : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Safely gets a decimal value from metadata.
    /// Returns null if the metadata is not found, the value is null, or cannot be parsed.
    /// </summary>
    public static decimal? GetDecimal(this IReadOnlyList<EventMetadata> metadata, string key)
    {
        var meta = metadata.FirstOrDefault(m => m.Key == key);
        if (meta?.Value is null)
            return null;

        if (meta.Value is decimal d)
            return d;

        if (meta.Value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
            {
                return jsonElement.TryGetDecimal(out var parsed) ? parsed : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Safely gets a double value from metadata.
    /// Returns null if the metadata is not found, the value is null, or cannot be parsed.
    /// </summary>
    public static double? GetDouble(this IReadOnlyList<EventMetadata> metadata, string key)
    {
        var meta = metadata.FirstOrDefault(m => m.Key == key);
        if (meta?.Value is null)
            return null;

        if (meta.Value is double dbl)
            return dbl;

        if (meta.Value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
            {
                return jsonElement.TryGetDouble(out var parsed) ? parsed : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Safely gets a boolean value from metadata.
    /// Returns null if the metadata is not found, the value is null, or cannot be parsed.
    /// </summary>
    public static bool? GetBoolean(this IReadOnlyList<EventMetadata> metadata, string key)
    {
        var meta = metadata.FirstOrDefault(m => m.Key == key);
        if (meta?.Value is null)
            return null;

        if (meta.Value is bool b)
            return b;

        if (meta.Value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return jsonElement.GetBoolean();
            }
        }

        return null;
    }

    /// <summary>
    /// Safely gets a Guid value from metadata with a specific source.
    /// Returns null if the metadata is not found with the specified source, the value is null, or cannot be parsed.
    /// </summary>
    public static Guid? GetGuid(this IReadOnlyList<EventMetadata> metadata, string key, string source)
    {
        var meta = metadata.FirstOrDefault(m => m.Key == key && m.Source == source);
        if (meta?.Value is null)
            return null;

        if (meta.Value is Guid guid)
            return guid;

        if (meta.Value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                return jsonElement.TryGetGuid(out var parsed) ? parsed : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the EventId from metadata.
    /// EventId is a system-generated unique identifier for each persisted event.
    /// </summary>
    public static Guid? GetEventId(this IReadOnlyList<EventMetadata> metadata)
    {
        return metadata.GetGuid(EventMetadataKeys.EventId);
    }

    /// <summary>
    /// Gets the EventId from System metadata source.
    /// EventId is always system-generated; use this method when you specifically need
    /// to ensure you're reading the system-generated EventId (e.g., for CausationId tracking).
    /// </summary>
    public static Guid? GetSystemEventId(this IReadOnlyList<EventMetadata> metadata)
    {
        return metadata.GetGuid(EventMetadataKeys.EventId, EventMetadataSource.System);
    }

    /// <summary>
    /// Gets the CorrelationId from metadata.
    /// CorrelationId tracks related events across aggregate boundaries.
    /// </summary>
    public static Guid? GetCorrelationId(this IReadOnlyList<EventMetadata> metadata)
    {
        return metadata.GetGuid(EventMetadataKeys.CorrelationId);
    }

    /// <summary>
    /// Gets the CausationId from metadata.
    /// CausationId references the EventId of the event that caused this event.
    /// </summary>
    public static Guid? GetCausationId(this IReadOnlyList<EventMetadata> metadata)
    {
        return metadata.GetGuid(EventMetadataKeys.CausationId);
    }

    /// <summary>
    /// Gets the BatchId from metadata.
    /// BatchId is shared by all events produced by a single command execution.
    /// </summary>
    public static Guid? GetBatchId(this IReadOnlyList<EventMetadata> metadata)
    {
        return metadata.GetGuid(EventMetadataKeys.BatchId);
    }

    /// <summary>
    /// Gets the Timestamp from metadata.
    /// Timestamp is the UTC time when the event was persisted to the event store.
    /// </summary>
    public static DateTime? GetTimestamp(this IReadOnlyList<EventMetadata> metadata)
    {
        return metadata.GetDateTime(EventMetadataKeys.Timestamp);
    }

    /// <summary>
    /// Gets the StreamVersion from metadata.
    /// StreamVersion is the version number of this event within its stream (1-indexed).
    /// </summary>
    public static long? GetStreamVersion(this IReadOnlyList<EventMetadata> metadata)
    {
        return metadata.GetInt64(EventMetadataKeys.StreamVersion);
    }
}

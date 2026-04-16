using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore.EntityFramework.Serialization;

/// <summary>
/// Unified serializer for event sourcing types.
/// Handles both registry-backed wire name serialization and plain typed JSON serialization.
/// </summary>
public sealed class EventStoreSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ITypeMetadataRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the EventStoreSerializer.
    /// </summary>
    /// <param name="registry">The type metadata registry.</param>
    public EventStoreSerializer(ITypeMetadataRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Serializes an object to JSON.
    /// </summary>
    public string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    /// <summary>
    /// Deserializes JSON to an object using its wire name for type resolution.
    /// The type must be registered in the metadata registry.
    /// </summary>
    public object Deserialize(string json, string wireName)
    {
        var type = _registry.GetTypeByWireName(wireName);
        if (type == null)
        {
            throw new InvalidOperationException(
                $"Cannot resolve type for wire name '{wireName}'. " +
                $"Ensure the type is registered in the ITypeMetadataRegistry.");
        }

        return JsonSerializer.Deserialize(json, type, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize type '{wireName}'");
    }

    /// <summary>
    /// Deserializes JSON to a known type without wire name resolution.
    /// </summary>
    public T Deserialize<T>(string json)
    {
        // Handle dynamic types by deserializing to JsonElement first
        if (typeof(T) == typeof(object) || typeof(T).Name == "Object")
        {
            var element = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
            return (T)(object)ConvertJsonElementToDynamic(element);
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize to type '{typeof(T).FullName}'");
    }

    /// <summary>
    /// Gets the wire name from an object using its metadata.
    /// The object's type must be decorated with an ITypeMetadata attribute and registered.
    /// </summary>
    public string GetWireName(object obj)
    {
        var type = obj.GetType();
        var metadata = _registry.GetMetadataByType(type);

        if (metadata?.WireName != null)
        {
            return metadata.WireName;
        }

        throw new InvalidOperationException(
            $"Type '{type.FullName}' is not registered in the ITypeMetadataRegistry or does not have a wire name. " +
            $"Ensure the type is decorated with an appropriate attribute (e.g., [Event], [Aggregate]) and the assembly is registered.");
    }

    /// <summary>
    /// Gets the wire name for a type without requiring an instance.
    /// </summary>
    public string GetWireName(Type type)
    {
        var metadata = _registry.GetMetadataByType(type);

        if (metadata?.WireName != null)
        {
            return metadata.WireName;
        }

        throw new InvalidOperationException(
            $"Type '{type.FullName}' is not registered in the ITypeMetadataRegistry or does not have a wire name. " +
            $"Ensure the type is decorated with an appropriate attribute (e.g., [Event], [Aggregate]) and the assembly is registered.");
    }

    private static dynamic ConvertJsonElementToDynamic(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => ConvertJsonArray(element),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => throw new InvalidOperationException($"Unsupported JSON value kind: {element.ValueKind}")
        };
    }

    private static dynamic ConvertJsonObject(JsonElement element)
    {
        var expando = new ExpandoObject();
        var dictionary = (IDictionary<string, object?>)expando;

        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonElementToDynamic(property.Value);
        }

        return expando;
    }

    private static dynamic ConvertJsonArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertJsonElementToDynamic(item));
        }
        return list;
    }
}

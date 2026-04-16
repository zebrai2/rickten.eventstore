using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore.EntityFramework.Serialization;

/// <summary>
/// Generic serializer that handles JSON serialization and type name resolution
/// based on attribute metadata.
/// </summary>
/// <typeparam name="TAttribute">The attribute type used for type name resolution.</typeparam>
public sealed class Serializer<TAttribute> where TAttribute : Attribute
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ITypeMetadataRegistry _registry;

    public Serializer(ITypeMetadataRegistry registry)
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
    /// Deserializes JSON to an object of the specified type.
    /// </summary>
    public object Deserialize(string json, string typeName)
    {
        var type = ResolveType(typeName);
        return JsonSerializer.Deserialize(json, type, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize type '{typeName}'");
    }

    /// <summary>
    /// Deserializes JSON to a known type.
    /// </summary>
    public T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize to type '{typeof(T).FullName}'");
    }

    /// <summary>
    /// Gets the type name from an object using the attribute metadata.
    /// </summary>
    public string GetTypeName(object obj)
    {
        var type = obj.GetType();
        var metadata = _registry.GetMetadataByType(type);

        if (metadata != null && metadata.AttributeType == typeof(TAttribute))
        {
            return metadata.WireName;
        }

        throw new InvalidOperationException(
            $"Type '{type.FullName}' must be decorated with [{typeof(TAttribute).Name}] attribute and registered in the ITypeMetadataRegistry.");
    }

    /// <summary>
    /// Resolves a type from its attribute-based name.
    /// </summary>
    private Type ResolveType(string typeName)
    {
        var type = _registry.GetTypeByWireName(typeName);
        if (type != null)
        {
            var metadata = _registry.GetMetadataByType(type);
            if (metadata != null && metadata.AttributeType == typeof(TAttribute))
            {
                return type;
            }
        }

        throw new InvalidOperationException(
            $"Cannot resolve type '{typeName}'. Ensure the type is decorated with [{typeof(TAttribute).Name}] attribute and registered in the ITypeMetadataRegistry.");
    }
}

/// <summary>
/// Non-generic serializer for cases where no attribute is needed (plain JSON).
/// </summary>
public static class Serializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes an object to JSON without type name resolution.
    /// </summary>
    public static string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    /// <summary>
    /// Deserializes JSON to a known type without type name resolution.
    /// </summary>
    public static T Deserialize<T>(string json)
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
        var expando = new System.Dynamic.ExpandoObject();
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

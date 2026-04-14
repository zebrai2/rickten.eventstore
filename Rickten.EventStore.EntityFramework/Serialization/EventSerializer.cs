using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rickten.EventStore.EntityFramework.Serialization;

/// <summary>
/// Handles serialization and deserialization of events with type information.
/// </summary>
internal static class EventSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly Dictionary<string, Type> TypeCache = new();

    /// <summary>
    /// Serializes an object to JSON.
    /// </summary>
    public static string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    /// <summary>
    /// Deserializes JSON to an object of the specified type.
    /// </summary>
    public static object Deserialize(string json, string typeName)
    {
        var type = ResolveType(typeName);
        return JsonSerializer.Deserialize(json, type, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize type '{typeName}'");
    }

    /// <summary>
    /// Deserializes JSON to a known type without requiring EventAttribute.
    /// Used for system types like metadata that don't need event versioning.
    /// </summary>
    public static T Deserialize<T>(string json)
    {
        // Special handling for object/dynamic to support dynamic property access
        if (typeof(T) == typeof(object))
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
            return (T)(object)JsonElementToExpandoObject(jsonElement);
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize to type '{typeof(T).FullName}'");
    }

    /// <summary>
    /// Converts a JsonElement to an ExpandoObject for dynamic property access.
    /// </summary>
    private static object JsonElementToExpandoObject(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var expando = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;
            foreach (var property in element.EnumerateObject())
            {
                expando[property.Name] = JsonElementToExpandoObject(property.Value);
            }
            return expando;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(JsonElementToExpandoObject).ToList();
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt32(out var intValue))
                return intValue;
            if (element.TryGetInt64(out var longValue))
                return longValue;
            return element.GetDouble();
        }
        else if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        else if (element.ValueKind == JsonValueKind.False)
        {
            return false;
        }
        else if (element.ValueKind == JsonValueKind.Null)
        {
            return null!;
        }
        return element;
    }

    /// <summary>
    /// Gets the type name from an object using EventAttribute.
    /// </summary>
    public static string GetTypeName(object obj)
    {
        var type = obj.GetType();
        var eventAttribute = type.GetCustomAttribute<EventAttribute>()
            ?? throw new InvalidOperationException(
                $"Event type '{type.FullName}' must be decorated with [Event] attribute.");

        return $"{eventAttribute.Aggregate}.{eventAttribute.Name}.v{eventAttribute.Version}";
    }

    /// <summary>
    /// Resolves a type from its EventAttribute-based name.
    /// </summary>
    private static Type ResolveType(string typeName)
    {
        if (TypeCache.TryGetValue(typeName, out var cachedType))
        {
            return cachedType;
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    var eventAttribute = type.GetCustomAttribute<EventAttribute>();
                    if (eventAttribute != null)
                    {
                        var attributeTypeName = $"{eventAttribute.Aggregate}.{eventAttribute.Name}.v{eventAttribute.Version}";
                        if (attributeTypeName == typeName)
                        {
                            TypeCache[typeName] = type;
                            return type;
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Skip assemblies that can't be loaded
            }
        }

        throw new InvalidOperationException(
            $"Cannot resolve event type '{typeName}'. Ensure the type is decorated with [Event] attribute and loaded in the current AppDomain.");
    }
}

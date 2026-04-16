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
    /// Gets the type name from an object using EventAttribute or AggregateAttribute.
    /// Events use [Event] ? "Aggregate.EventName.vVersion"
    /// States use [Aggregate] ? "Aggregate.StateName"
    /// Objects without attributes use FullName as fallback.
    /// </summary>
    public static string GetTypeName(object obj)
    {
        var type = obj.GetType();

        // Check for [Event] attribute first (for events)
        var eventAttribute = type.GetCustomAttribute<EventAttribute>();
        if (eventAttribute != null)
        {
            return $"{eventAttribute.Aggregate}.{eventAttribute.Name}.v{eventAttribute.Version}";
        }

        // Check for [Aggregate] attribute (for states) - use reflection to avoid assembly reference
        var aggregateAttribute = type.GetCustomAttributes(inherit: false)
            .FirstOrDefault(attr => attr.GetType().Name == "AggregateAttribute");
        if (aggregateAttribute != null)
        {
            var aggregateName = aggregateAttribute.GetType().GetProperty("Name")?.GetValue(aggregateAttribute) as string;
            if (aggregateName != null)
            {
                return $"{aggregateName}.{type.Name}";
            }
        }

        // Fallback to FullName for objects without attributes
        return type.FullName 
            ?? throw new InvalidOperationException($"Type has no FullName");
    }

    /// <summary>
    /// Resolves a type from its EventAttribute-based name, AggregateAttribute-based name, or FullName.
    /// </summary>
    private static Type ResolveType(string typeName)
    {
        if (TypeCache.TryGetValue(typeName, out var cachedType))
        {
            return cachedType;
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        // First, try as type FullName (for backward compatibility)
        foreach (var assembly in assemblies)
        {
            try
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    TypeCache[typeName] = type;
                    return type;
                }
            }
            catch (Exception)
            {
                // Skip assemblies that fail
            }
        }

        // Try attribute-based resolution
        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    // Check [Event] attribute
                    var eventAttribute = type.GetCustomAttribute<EventAttribute>();
                    if (eventAttribute != null)
                    {
                        var eventTypeName = $"{eventAttribute.Aggregate}.{eventAttribute.Name}.v{eventAttribute.Version}";
                        if (eventTypeName == typeName)
                        {
                            TypeCache[typeName] = type;
                            return type;
                        }
                    }

                    // Check [Aggregate] attribute (for states) - use reflection to avoid assembly reference
                    var aggregateAttribute = type.GetCustomAttributes(inherit: false)
                        .FirstOrDefault(attr => attr.GetType().Name == "AggregateAttribute");
                    if (aggregateAttribute != null)
                    {
                        var aggregateName = aggregateAttribute.GetType().GetProperty("Name")?.GetValue(aggregateAttribute) as string;
                        if (aggregateName != null)
                        {
                            var aggregateTypeName = $"{aggregateName}.{type.Name}";
                            if (aggregateTypeName == typeName)
                            {
                                TypeCache[typeName] = type;
                                return type;
                            }
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
            $"Cannot resolve type '{typeName}'. Ensure the type is loaded in the current AppDomain.");
    }
}

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rickten.EventStore.EntityFramework.Serialization;

/// <summary>
/// Generic serializer that handles JSON serialization and type name resolution
/// based on attribute metadata.
/// </summary>
/// <typeparam name="TAttribute">The attribute type used for type name resolution.</typeparam>
public static class Serializer<TAttribute> where TAttribute : Attribute
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
    /// Deserializes JSON to a known type.
    /// </summary>
    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize to type '{typeof(T).FullName}'");
    }

    /// <summary>
    /// Gets the type name from an object using the attribute metadata.
    /// </summary>
    public static string GetTypeName(object obj)
    {
        var type = obj.GetType();
        var attribute = type.GetCustomAttribute<TAttribute>();

        if (attribute == null)
        {
            throw new InvalidOperationException(
                $"Type '{type.FullName}' must be decorated with [{typeof(TAttribute).Name}] attribute.");
        }

        return GetTypeNameFromAttribute(type, attribute);
    }

    /// <summary>
    /// Resolves a type from its attribute-based name.
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
                    var attribute = type.GetCustomAttribute<TAttribute>();
                    if (attribute != null)
                    {
                        var attributeTypeName = GetTypeNameFromAttribute(type, attribute);
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
            $"Cannot resolve type '{typeName}'. Ensure the type is decorated with [{typeof(TAttribute).Name}] attribute and loaded in the current AppDomain.");
    }

    /// <summary>
    /// Gets the type name from an attribute. Override this for different naming schemes.
    /// </summary>
    private static string GetTypeNameFromAttribute(Type type, TAttribute attribute)
    {
        // Use reflection to get the naming strategy based on attribute properties
        return attribute switch
        {
            // EventAttribute: "Aggregate.Name.vVersion"
            _ when typeof(TAttribute).Name == "EventAttribute" =>
                GetEventTypeName(type, attribute),

            // AggregateAttribute: "AggregateName.TypeName"
            _ when typeof(TAttribute).Name == "AggregateAttribute" =>
                GetAggregateTypeName(type, attribute),

            // ProjectionAttribute: "ProjectionName.TypeName"
            _ when typeof(TAttribute).Name == "ProjectionAttribute" =>
                GetProjectionTypeName(type, attribute),

            // Fallback: use FullName
            _ => type.FullName ?? throw new InvalidOperationException($"Type '{type}' has no FullName")
        };
    }

    private static string GetEventTypeName(Type type, TAttribute attribute)
    {
        var aggregate = attribute.GetType().GetProperty("Aggregate")?.GetValue(attribute) as string;
        var name = attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string;
        var version = attribute.GetType().GetProperty("Version")?.GetValue(attribute);

        if (aggregate == null || name == null || version == null)
        {
            throw new InvalidOperationException(
                $"EventAttribute on type '{type.FullName}' must have Aggregate, Name, and Version properties.");
        }

        return $"{aggregate}.{name}.v{version}";
    }

    private static string GetAggregateTypeName(Type type, TAttribute attribute)
    {
        var aggregateName = attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string;

        if (aggregateName == null)
        {
            throw new InvalidOperationException(
                $"AggregateAttribute on type '{type.FullName}' must have a Name property.");
        }

        return $"{aggregateName}.{type.Name}";
    }

    private static string GetProjectionTypeName(Type type, TAttribute attribute)
    {
        var projectionName = attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string;

        if (projectionName == null)
        {
            throw new InvalidOperationException(
                $"ProjectionAttribute on type '{type.FullName}' must have a Name property.");
        }

        return $"{projectionName}.{type.Name}";
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

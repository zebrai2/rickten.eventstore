using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rickten.EventStore.EntityFramework.Serialization;

/// <summary>
/// Serializer for aggregate state objects.
/// States must be decorated with [Aggregate] attribute.
/// </summary>
public static class StateSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly Dictionary<string, Type> TypeCache = new();

    /// <summary>
    /// Serializes a state object to JSON.
    /// </summary>
    public static string Serialize(object state)
    {
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    /// <summary>
    /// Deserializes JSON to a state object by type name.
    /// </summary>
    public static object Deserialize(string json, string typeName)
    {
        var type = ResolveType(typeName);
        return JsonSerializer.Deserialize(json, type, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize state type '{typeName}'");
    }

    /// <summary>
    /// Gets the type name from a state object using [Aggregate] attribute.
    /// Format: "AggregateName.TypeName"
    /// Falls back to FullName for backward compatibility.
    /// </summary>
    public static string GetTypeName(object state)
    {
        var type = state.GetType();

        // Try to find [Aggregate] attribute using reflection (avoid assembly reference)
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

        // Fallback to FullName for backward compatibility (states without [Aggregate])
        return type.FullName 
            ?? throw new InvalidOperationException($"State type has no FullName");
    }

    /// <summary>
    /// Resolves a type from its type name.
    /// Supports both "Aggregate.TypeName" format and FullName fallback.
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

        // Try to resolve by [Aggregate] attribute
        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
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
            $"Cannot resolve state type '{typeName}'. Ensure the type is loaded in the current AppDomain.");
    }
}

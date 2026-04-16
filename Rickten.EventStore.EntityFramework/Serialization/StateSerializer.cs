using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore.EntityFramework.Serialization;

/// <summary>
/// Serializer for aggregate state objects.
/// States must be decorated with [Aggregate] attribute.
/// </summary>
public sealed class StateSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ITypeMetadataRegistry _registry;

    public StateSerializer(ITypeMetadataRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Serializes a state object to JSON.
    /// </summary>
    public string Serialize(object state)
    {
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    /// <summary>
    /// Deserializes JSON to a state object by type name.
    /// </summary>
    public object Deserialize(string json, string typeName)
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
    public string GetTypeName(object state)
    {
        var type = state.GetType();
        var metadata = _registry.GetMetadataByType(type);

        if (metadata != null && metadata.AttributeType.Name == "AggregateAttribute")
        {
            return metadata.WireName;
        }

        // Fallback to FullName for backward compatibility (states without [Aggregate])
        return type.FullName 
            ?? throw new InvalidOperationException($"State type has no FullName");
    }

    /// <summary>
    /// Resolves a type from its type name.
    /// Supports both "Aggregate.TypeName" format and FullName fallback.
    /// </summary>
    private Type ResolveType(string typeName)
    {
        // Try registry first
        var type = _registry.GetTypeByWireName(typeName);
        if (type != null)
        {
            return type;
        }

        // Fallback: try as type FullName (for backward compatibility)
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                var fallbackType = assembly.GetType(typeName);
                if (fallbackType != null)
                {
                    return fallbackType;
                }
            }
            catch (Exception)
            {
                // Skip assemblies that fail
            }
        }

        throw new InvalidOperationException(
            $"Cannot resolve state type '{typeName}'. Ensure the type is registered in the ITypeMetadataRegistry or is a valid FullName.");
    }
}

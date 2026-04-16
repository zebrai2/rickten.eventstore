using System.Text.Json;
using System.Text.Json.Serialization;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore.EntityFramework.Serialization;

/// <summary>
/// Serializer for aggregate state objects.
/// All state types must be decorated with [Aggregate] attribute and registered in ITypeMetadataRegistry.
/// Type resolution is explicit and registry-driven; unregistered types will fail with clear exceptions.
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
    /// The state type must be registered in the ITypeMetadataRegistry.
    /// </summary>
    public string GetTypeName(object state)
    {
        var type = state.GetType();
        var metadata = _registry.GetMetadataByType(type);

        if (metadata != null && metadata.AttributeType.Name == "AggregateAttribute")
        {
            return metadata.WireName;
        }

        throw new InvalidOperationException(
            $"State type '{type.FullName}' is not registered in the ITypeMetadataRegistry. " +
            $"Ensure the assembly containing this type is registered during startup using TypeMetadataRegistryBuilder.AddAssembly().");
    }

    /// <summary>
    /// Resolves a type from its type name.
    /// The type name must be registered in the ITypeMetadataRegistry using the "Aggregate.TypeName" format.
    /// </summary>
    private Type ResolveType(string typeName)
    {
        var type = _registry.GetTypeByWireName(typeName);
        if (type != null)
        {
            return type;
        }

        throw new InvalidOperationException(
            $"Cannot resolve state type '{typeName}'. The type is not registered in the ITypeMetadataRegistry. " +
            $"Ensure the assembly containing this type is registered during startup using TypeMetadataRegistryBuilder.AddAssembly().");
    }
}

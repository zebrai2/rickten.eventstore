using System.Text.Json;
using System.Text.Json.Serialization;
using Rickten.EventStore.TypeMetadata;

namespace Rickten.EventStore.EntityFramework.Serialization;

/// <summary>
/// Unified serializer for wire-type-based serialization.
/// Translates between CLR types, wire types, and JSON payloads using the type metadata registry.
/// All persistence operations use wire types for self-describing storage.
/// </summary>
public sealed class WireTypeSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ITypeMetadataRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the WireTypeSerializer.
    /// </summary>
    /// <param name="registry">The type metadata registry.</param>
    public WireTypeSerializer(ITypeMetadataRegistry registry)
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
    /// Deserializes JSON to a known infrastructure type that doesn't participate in the wire-type model.
    /// This is for internal types like EventMetadata that are not domain types.
    /// </summary>
    /// <typeparam name="T">The infrastructure type to deserialize to.</typeparam>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized object.</returns>
    internal T DeserializeInfrastructure<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize infrastructure type '{typeof(T).FullName}'");
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
            $"Ensure the type is decorated with an appropriate attribute (e.g., [Event], [Aggregate], [Projection]) and the assembly is registered.");
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
            $"Ensure the type is decorated with an appropriate attribute (e.g., [Event], [Aggregate], [Projection]) and the assembly is registered.");
    }
}

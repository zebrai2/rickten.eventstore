using System.Collections.Concurrent;
using System.Reflection;

namespace Rickten.EventStore.TypeMetadata;

/// <summary>
/// Default implementation of ITypeMetadataRegistry.
/// Built once at startup from explicitly registered assemblies, then provides readonly lookups.
/// </summary>
public sealed class TypeMetadataRegistry : ITypeMetadataRegistry
{
    private readonly IReadOnlyDictionary<Type, TypeMetadata> _typeToMetadata;
    private readonly IReadOnlyDictionary<string, Type> _wireNameToType;
    private readonly IReadOnlyDictionary<string, IReadOnlyCollection<Type>> _aggregateToEventTypes;
    private readonly IReadOnlyCollection<TypeMetadata> _allMetadata;

    /// <summary>
    /// Initializes a new instance of the TypeMetadataRegistry.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for attributed types.</param>
    public TypeMetadataRegistry(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var allMetadataList = new List<TypeMetadata>();
        var typeToMetadata = new Dictionary<Type, TypeMetadata>();
        var wireNameToType = new Dictionary<string, Type>();
        var aggregateToEventTypes = new Dictionary<string, HashSet<Type>>();

        foreach (var assembly in assemblies)
        {
            ScanAssembly(assembly, allMetadataList, typeToMetadata, wireNameToType, aggregateToEventTypes);
        }

        _allMetadata = allMetadataList.AsReadOnly();
        _typeToMetadata = typeToMetadata;
        _wireNameToType = wireNameToType;
        _aggregateToEventTypes = aggregateToEventTypes.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyCollection<Type>)kvp.Value.ToList().AsReadOnly());
    }

    /// <inheritdoc />
    public TypeMetadata? GetMetadataByType(Type type)
    {
        return _typeToMetadata.TryGetValue(type, out var metadata) ? metadata : null;
    }

    /// <inheritdoc />
    public Type? GetTypeByWireName(string wireName)
    {
        return _wireNameToType.TryGetValue(wireName, out var type) ? type : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Type> GetEventTypesForAggregate(string aggregateName)
    {
        return _aggregateToEventTypes.TryGetValue(aggregateName, out var types)
            ? types
            : Array.Empty<Type>();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<TypeMetadata> GetAllMetadata()
    {
        return _allMetadata;
    }

    private static void ScanAssembly(
        Assembly assembly,
        List<TypeMetadata> allMetadataList,
        Dictionary<Type, TypeMetadata> typeToMetadata,
        Dictionary<string, Type> wireNameToType,
        Dictionary<string, HashSet<Type>> aggregateToEventTypes)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).ToArray()!;
        }

        foreach (var type in types)
        {
            // Look for any attribute implementing ITypeMetadata
            var metadataAttr = type.GetCustomAttributes(inherit: false)
                .OfType<ITypeMetadata>()
                .FirstOrDefault();

            if (metadataAttr == null) continue;

            ProcessTypeMetadata(type, metadataAttr, allMetadataList, typeToMetadata, wireNameToType, aggregateToEventTypes);
        }
    }

    private static void ProcessTypeMetadata(
        Type type,
        ITypeMetadata metadataAttr,
        List<TypeMetadata> allMetadataList,
        Dictionary<Type, TypeMetadata> typeToMetadata,
        Dictionary<string, Type> wireNameToType,
        Dictionary<string, HashSet<Type>> aggregateToEventTypes)
    {
        var wireName = metadataAttr.GetWireName(type);
        if (wireName == null)
        {
            // Type doesn't participate in wire name serialization (e.g., some projections)
            // Still register it for metadata lookup
            var metadataWithoutWireName = new TypeMetadata
            {
                ClrType = type,
                WireName = null,
                AggregateName = metadataAttr.GetAggregateName(),
                AttributeType = metadataAttr.GetType(),
                AttributeInstance = (Attribute)metadataAttr
            };

            allMetadataList.Add(metadataWithoutWireName);
            typeToMetadata[type] = metadataWithoutWireName;
            return;
        }

        if (wireNameToType.TryGetValue(wireName, out var existingType))
        {
            throw new InvalidOperationException(
                $"Duplicate wire name '{wireName}' found for types '{existingType.FullName}' and '{type.FullName}'. " +
                $"Each type must have a unique wire name.");
        }

        var metadata = new TypeMetadata
        {
            ClrType = type,
            WireName = wireName,
            AggregateName = metadataAttr.GetAggregateName(),
            AttributeType = metadataAttr.GetType(),
            AttributeInstance = (Attribute)metadataAttr
        };

        allMetadataList.Add(metadata);
        typeToMetadata[type] = metadata;
        wireNameToType[wireName] = type;

        // Track event types by aggregate
        if (metadataAttr is EventAttribute && metadata.AggregateName != null)
        {
            if (!aggregateToEventTypes.ContainsKey(metadata.AggregateName))
            {
                aggregateToEventTypes[metadata.AggregateName] = new HashSet<Type>();
            }
            aggregateToEventTypes[metadata.AggregateName].Add(type);
        }
    }
}

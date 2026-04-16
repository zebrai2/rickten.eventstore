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
            ProcessEventAttribute(type, allMetadataList, typeToMetadata, wireNameToType, aggregateToEventTypes);
            ProcessAggregateAttribute(type, allMetadataList, typeToMetadata, wireNameToType);
            ProcessCommandAttribute(type, allMetadataList, typeToMetadata, wireNameToType);
        }
    }

    private static void ProcessEventAttribute(
        Type type,
        List<TypeMetadata> allMetadataList,
        Dictionary<Type, TypeMetadata> typeToMetadata,
        Dictionary<string, Type> wireNameToType,
        Dictionary<string, HashSet<Type>> aggregateToEventTypes)
    {
        var eventAttr = type.GetCustomAttribute<EventAttribute>();
        if (eventAttr == null) return;

        var wireName = $"{eventAttr.Aggregate}.{eventAttr.Name}.v{eventAttr.Version}";

        if (wireNameToType.TryGetValue(wireName, out var existingType))
        {
            throw new InvalidOperationException(
                $"Duplicate wire name '{wireName}' found for types '{existingType.FullName}' and '{type.FullName}'. " +
                $"Each [Event] must have a unique (Aggregate, Name, Version) combination.");
        }

        var metadata = new TypeMetadata
        {
            ClrType = type,
            WireName = wireName,
            AggregateName = eventAttr.Aggregate,
            AttributeType = typeof(EventAttribute),
            AttributeInstance = eventAttr
        };

        allMetadataList.Add(metadata);
        typeToMetadata[type] = metadata;
        wireNameToType[wireName] = type;

        if (!aggregateToEventTypes.ContainsKey(eventAttr.Aggregate))
        {
            aggregateToEventTypes[eventAttr.Aggregate] = new HashSet<Type>();
        }
        aggregateToEventTypes[eventAttr.Aggregate].Add(type);
    }

    private static void ProcessAggregateAttribute(
        Type type,
        List<TypeMetadata> allMetadataList,
        Dictionary<Type, TypeMetadata> typeToMetadata,
        Dictionary<string, Type> wireNameToType)
    {
        // Check for AggregateAttribute from Rickten.Aggregator
        var aggregateAttr = type.GetCustomAttributes(inherit: false)
            .FirstOrDefault(attr => attr.GetType().Name == "AggregateAttribute");

        if (aggregateAttr == null) return;

        var aggregateName = aggregateAttr.GetType().GetProperty("Name")?.GetValue(aggregateAttr) as string;
        if (aggregateName == null) return;

        var wireName = $"{aggregateName}.{type.Name}";

        if (wireNameToType.TryGetValue(wireName, out var existingType))
        {
            throw new InvalidOperationException(
                $"Duplicate wire name '{wireName}' found for types '{existingType.FullName}' and '{type.FullName}'. " +
                $"Each [Aggregate] state type must have a unique (Name, TypeName) combination.");
        }

        var metadata = new TypeMetadata
        {
            ClrType = type,
            WireName = wireName,
            AggregateName = aggregateName,
            AttributeType = aggregateAttr.GetType(),
            AttributeInstance = (Attribute)aggregateAttr
        };

        allMetadataList.Add(metadata);
        typeToMetadata[type] = metadata;
        wireNameToType[wireName] = type;
    }

    private static void ProcessCommandAttribute(
        Type type,
        List<TypeMetadata> allMetadataList,
        Dictionary<Type, TypeMetadata> typeToMetadata,
        Dictionary<string, Type> wireNameToType)
    {
        // Check for CommandAttribute from Rickten.Aggregator
        var commandAttr = type.GetCustomAttributes(inherit: false)
            .FirstOrDefault(attr => attr.GetType().Name == "CommandAttribute");

        if (commandAttr == null) return;

        var aggregateName = commandAttr.GetType().GetProperty("Aggregate")?.GetValue(commandAttr) as string;
        if (aggregateName == null) return;

        var wireName = $"{aggregateName}.{type.Name}";

        if (wireNameToType.TryGetValue(wireName, out var existingType))
        {
            throw new InvalidOperationException(
                $"Duplicate wire name '{wireName}' found for types '{existingType.FullName}' and '{type.FullName}'. " +
                $"Each [Command] type must have a unique (Aggregate, TypeName) combination.");
        }

        var metadata = new TypeMetadata
        {
            ClrType = type,
            WireName = wireName,
            AggregateName = aggregateName,
            AttributeType = commandAttr.GetType(),
            AttributeInstance = (Attribute)commandAttr
        };

        allMetadataList.Add(metadata);
        typeToMetadata[type] = metadata;
        wireNameToType[wireName] = type;
    }
}

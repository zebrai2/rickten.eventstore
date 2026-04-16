using System.Reflection;

namespace Rickten.EventStore.TypeMetadata;

/// <summary>
/// Builder for constructing an ITypeMetadataRegistry with explicit assembly registration.
/// </summary>
public sealed class TypeMetadataRegistryBuilder
{
    private readonly HashSet<Assembly> _assemblies = new();

    /// <summary>
    /// Adds an assembly to be scanned for attributed types.
    /// </summary>
    public TypeMetadataRegistryBuilder AddAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Adds the assembly containing the specified type.
    /// </summary>
    public TypeMetadataRegistryBuilder AddAssemblyContaining<T>()
    {
        return AddAssembly(typeof(T).Assembly);
    }

    /// <summary>
    /// Adds the assembly containing the specified type.
    /// </summary>
    public TypeMetadataRegistryBuilder AddAssemblyContaining(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return AddAssembly(type.Assembly);
    }

    /// <summary>
    /// Adds multiple assemblies to be scanned.
    /// </summary>
    public TypeMetadataRegistryBuilder AddAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var assembly in assemblies)
        {
            _assemblies.Add(assembly);
        }
        return this;
    }

    /// <summary>
    /// Builds the registry from the registered assemblies.
    /// </summary>
    public ITypeMetadataRegistry Build()
    {
        return new TypeMetadataRegistry(_assemblies);
    }
}

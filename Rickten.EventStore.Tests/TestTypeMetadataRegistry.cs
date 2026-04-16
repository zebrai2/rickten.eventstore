using Rickten.EventStore.TypeMetadata;
using System;
using System.Reflection;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Helper for creating type metadata registries in tests.
/// </summary>
internal static class TestTypeMetadataRegistry
{
    /// <summary>
    /// Creates a registry with the test assembly and any additional assemblies.
    /// </summary>
    public static ITypeMetadataRegistry Create(params Assembly[] additionalAssemblies)
    {
        var builder = new TypeMetadataRegistryBuilder()
            .AddAssembly(typeof(TestTypeMetadataRegistry).Assembly);

        if (additionalAssemblies != null)
        {
            builder.AddAssemblies(additionalAssemblies);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a registry with types from the specified marker types.
    /// </summary>
    public static ITypeMetadataRegistry CreateFromTypes(params Type[] markerTypes)
    {
        var builder = new TypeMetadataRegistryBuilder()
            .AddAssembly(typeof(TestTypeMetadataRegistry).Assembly);

        foreach (var type in markerTypes)
        {
            builder.AddAssembly(type.Assembly);
        }

        return builder.Build();
    }
}

using Rickten.EventStore.TypeMetadata;
using Rickten.Aggregator;
using Rickten.EventStore.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Tests for duplicate metadata identity detection and hardening.
/// These tests verify that duplicate wire names/metadata identities fail fast during registry construction,
/// while duplicate assembly registrations are safely deduped.
/// </summary>
public class TypeMetadataRegistryDuplicateTests
{
    [Fact]
    public void Registry_WithDuplicateAssemblyRegistration_DoesNotThrow()
    {
        // Arrange: Register the same assembly multiple times
        var assembly = typeof(DuplicateTestEventOne).Assembly;

        // Act: Build registry with duplicate assembly registrations
        var registry = new TypeMetadataRegistryBuilder()
            .AddAssembly(assembly)
            .AddAssembly(assembly) // Same assembly again
            .AddAssembly(assembly) // And again
            .Build();

        // Assert: Should succeed - duplicate assemblies are deduped by HashSet
        Assert.NotNull(registry);
        var metadata = registry.GetMetadataByType(typeof(DuplicateTestEventOne));
        Assert.NotNull(metadata);
        Assert.Equal("DuplicateTest.EventOne.v1", metadata.WireName);
    }

    [Fact]
    public void Registry_WithDuplicateWireNameInDynamicAssembly_ThrowsInvalidOperationException()
    {
        // Arrange: Create two types with the same wire name in a dynamic assembly
        var assembly = CreateDynamicAssemblyWithDuplicateWireNames();

        // Act & Assert: Registry construction should throw with clear error
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            new TypeMetadataRegistry(new[] { assembly });
        });

        // Verify error message quality
        Assert.Contains("Duplicate wire name", exception.Message);
        Assert.Contains("DuplicateAggregate.DuplicateEvent.v1", exception.Message);
        Assert.Contains("Each type must have a unique wire name", exception.Message);
    }

    [Fact]
    public void Registry_WithDistinctMetadataAcrossMultipleAssemblies_Succeeds()
    {
        // Arrange: Multiple assemblies with distinct metadata
        var assembly1 = typeof(DuplicateTestEventOne).Assembly;
        var assembly2 = typeof(TypeMetadataRegistry).Assembly; // Different assembly

        // Act: Build registry with multiple assemblies
        var registry = new TypeMetadataRegistryBuilder()
            .AddAssembly(assembly1)
            .AddAssembly(assembly2)
            .Build();

        // Assert: Should succeed - all metadata identities are distinct
        Assert.NotNull(registry);
        Assert.NotNull(registry.GetMetadataByType(typeof(DuplicateTestEventOne)));
    }

    [Fact]
    public void Registry_MergedMetadataWithDistinctIdentities_ReturnsAllTypes()
    {
        // Arrange: Simulate merged registration scenario
        var assembly = typeof(DuplicateTestEventOne).Assembly;

        // Act: Build registry (simulating multiple AddEventStore calls)
        var builder = new TypeMetadataRegistryBuilder();
        builder.AddAssembly(assembly);
        builder.AddAssemblies(new[] { assembly }); // Merge scenario
        var registry = builder.Build();

        // Assert: All distinct types should be accessible
        Assert.NotNull(registry.GetTypeByWireName("DuplicateTest.EventOne.v1"));
        Assert.NotNull(registry.GetTypeByWireName("DuplicateTest.EventTwo.v2"));
        Assert.NotNull(registry.GetTypeByWireName("DuplicateTest.DuplicateTestAggregateState"));

        // Verify correct count
        var eventTypes = registry.GetEventTypesForAggregate("DuplicateTest");
        Assert.Equal(2, eventTypes.Count);
    }

    [Fact]
    public void Registry_ErrorMessage_IncludesBothConflictingTypeNames()
    {
        // Arrange: Create types with duplicate wire names
        var assembly = CreateDynamicAssemblyWithDuplicateWireNames();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            new TypeMetadataRegistry(new[] { assembly });
        });

        // Verify both type names are mentioned for debugging
        var message = exception.Message;
        Assert.Contains("Type1", message); // First type name
        Assert.Contains("Type2", message); // Second type name
    }

    [Fact]
    public void Registry_WithTypesFromSameAssembly_AllRegisteredCorrectly()
    {
        // Arrange: Multiple types from the same assembly with distinct wire names
        var assembly = typeof(DuplicateTestEventOne).Assembly;

        // Act: Build registry
        var registry = new TypeMetadataRegistryBuilder()
            .AddAssembly(assembly)
            .Build();

        // Assert: All types should be registered correctly
        Assert.NotNull(registry);

        // Verify all test types are accessible
        Assert.NotNull(registry.GetMetadataByType(typeof(DuplicateTestEventOne)));
        Assert.NotNull(registry.GetMetadataByType(typeof(DuplicateTestEventTwo)));
        Assert.NotNull(registry.GetMetadataByType(typeof(DuplicateTestAggregateState)));

        // Verify wire names are correctly formed
        Assert.Equal(typeof(DuplicateTestEventOne), registry.GetTypeByWireName("DuplicateTest.EventOne.v1"));
        Assert.Equal(typeof(DuplicateTestEventTwo), registry.GetTypeByWireName("DuplicateTest.EventTwo.v2"));
    }

    [Fact]
    public void AddEventStore_WithDuplicateAssembliesAcrossCalls_DoesNotThrow()
    {
        // Arrange: Simulate multiple AddEventStore calls with overlapping assemblies
        var services = new ServiceCollection();
        var assembly = typeof(DuplicateTestEventOne).Assembly;

        // Act: Call AddEventStore multiple times with the same assembly
        services.AddEventStoreInMemory<DuplicateTestEventOne>("TestDb1");
        services.AddEventStoreInMemory("TestDb2", assembly);
        services.AddEventStoreInMemory("TestDb3", assembly);

        // Build the service provider to trigger registry construction
        var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<ITypeMetadataRegistry>();

        // Assert: Should succeed - duplicate assemblies are deduped
        Assert.NotNull(registry);
        var metadata = registry.GetMetadataByType(typeof(DuplicateTestEventOne));
        Assert.NotNull(metadata);
        Assert.Equal("DuplicateTest.EventOne.v1", metadata.WireName);
    }

    [Fact]
    public void AddEventStore_WithDuplicateWireNamesAcrossAssemblies_ThrowsOnBuild()
    {
        // Arrange: Create an assembly with duplicate wire names and add it via DI
        var services = new ServiceCollection();
        var duplicateAssembly = CreateDynamicAssemblyWithDuplicateWireNames();

        // Act & Assert: Building the service provider should throw when registry is constructed
        services.AddEventStoreInMemory("TestDb", duplicateAssembly);

        var serviceProvider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            serviceProvider.GetRequiredService<ITypeMetadataRegistry>();
        });

        // Verify error message
        Assert.Contains("Duplicate wire name", exception.Message);
        Assert.Contains("DuplicateAggregate.DuplicateEvent.v1", exception.Message);
    }

    /// <summary>
    /// Helper method to create a dynamic assembly with duplicate wire names for testing.
    /// This simulates the scenario where two different types would produce the same wire type.
    /// </summary>
    private static Assembly CreateDynamicAssemblyWithDuplicateWireNames()
    {
        var assemblyName = new AssemblyName($"DuplicateTestAssembly_{Guid.NewGuid()}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

        // Create first type with event attribute
        var type1 = moduleBuilder.DefineType("Type1", TypeAttributes.Public);
        var eventAttr1 = new EventAttribute("DuplicateAggregate", "DuplicateEvent", 1);
        var attrBuilder1 = new CustomAttributeBuilder(
            typeof(EventAttribute).GetConstructor(new[] { typeof(string), typeof(string), typeof(int) })!,
            new object[] { "DuplicateAggregate", "DuplicateEvent", 1 });
        type1.SetCustomAttribute(attrBuilder1);
        type1.CreateType();

        // Create second type with SAME wire name
        var type2 = moduleBuilder.DefineType("Type2", TypeAttributes.Public);
        var attrBuilder2 = new CustomAttributeBuilder(
            typeof(EventAttribute).GetConstructor(new[] { typeof(string), typeof(string), typeof(int) })!,
            new object[] { "DuplicateAggregate", "DuplicateEvent", 1 }); // Same wire name!
        type2.SetCustomAttribute(attrBuilder2);
        type2.CreateType();

        return assemblyBuilder;
    }
}

// Test domain types for duplicate detection tests

[Event("DuplicateTest", "EventOne", 1)]
public record DuplicateTestEventOne(string Data);

[Event("DuplicateTest", "EventTwo", 2)]
public record DuplicateTestEventTwo(int Value);

[Aggregate("DuplicateTest")]
public record DuplicateTestAggregateState(int Count);

// Projection with null wire name (doesn't implement ITypeMetadata that provides wire name)
// Note: We'll use a custom attribute for testing purposes
public record DuplicateTestProjection();

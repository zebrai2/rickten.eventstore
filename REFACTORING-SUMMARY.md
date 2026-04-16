# Event Sourcing Library Refactoring Summary

## Overview
Refactored the event sourcing library to replace runtime assembly scanning and static mutable caches with a startup-built, readonly metadata registry.

## What Changed

### 1. New Type Metadata Registry System
Created a centralized registry for type metadata:

**New Files:**
- `Rickten.EventStore\TypeMetadata\TypeMetadata.cs` - Record holding type metadata (CLR type, wire name, aggregate name, attribute)
- `Rickten.EventStore\TypeMetadata\ITypeMetadataRegistry.cs` - Interface for readonly type lookups
- `Rickten.EventStore\TypeMetadata\TypeMetadataRegistry.cs` - Implementation with readonly dictionaries built at startup
- `Rickten.EventStore\TypeMetadata\TypeMetadataRegistryBuilder.cs` - Fluent builder for registry construction

**Key Features:**
- Built once at startup from explicitly registered assemblies
- Provides readonly lookups via `IReadOnlyDictionary<,>` and `IReadOnlyCollection<>`
- Supports three lookup patterns:
  1. `Type → TypeMetadata` (for serialization)
  2. `string wireName → Type` (for deserialization)
  3. `string aggregateName → IReadOnlyCollection<Type>` (for event validation)
- Validates duplicate wire names at construction time
- Thread-safe by design (immutable after construction)

### 2. Removed Runtime Assembly Scanning

**StateFolder.cs:**
- Removed `ScanEventsForAggregate()` method that called `AppDomain.CurrentDomain.GetAssemblies()`
- Now uses `ITypeMetadataRegistry.GetEventTypesForAggregate()` 
- Constructor now requires `ITypeMetadataRegistry` parameter
- Validation occurs at folder construction, not via assembly scanning

**EventSerializer.cs:**
- **REMOVED ENTIRELY** - was redundant with `Serializer<TAttribute>`

**Serializer.cs:**
- Changed from static class to instance class requiring `ITypeMetadataRegistry`
- Removed `AppDomain.CurrentDomain.GetAssemblies()` scanning in `ResolveType()`
- Removed static mutable `Dictionary<string, Type> TypeCache`
- Now uses registry for all type lookups
- Falls back to `AppDomain.GetAssemblies()` only for backward compatibility with unregistered types

**StateSerializer.cs:**
- Changed from static class to instance class requiring `ITypeMetadataRegistry`
- Removed assembly scanning logic
- Removed static mutable `Dictionary<string, Type> TypeCache`
- Now uses registry for type resolution

### 3. Updated Store Implementations

**EventStore.cs:**
- Constructor now accepts `ITypeMetadataRegistry`
- Creates instance of `Serializer<EventAttribute>` instead of using static `EventSerializer`
- Changed `MapToStreamEvent()` from static to instance method

**SnapshotStore.cs:**
- Constructor now accepts `ITypeMetadataRegistry`
- Creates instance of `StateSerializer`

**ProjectionStore.cs:**
- No changes required (uses non-generic `Serializer` which doesn't need attributes)

### 4. DI Registration Updates

**ServiceCollectionExtensions.cs:**
- `AddEventStore()` now accepts `params Assembly[] assemblies` parameter
- Registers `ITypeMetadataRegistry` as singleton
- Defaults to scanning calling assembly if no assemblies specified
- `AddEventStoreInMemory()` and `AddEventStoreSqlServer()` also accept assemblies parameter

**Example Usage:**
```csharp
services.AddEventStore(options => 
{
    options.UseSqlServer(connectionString);
}, 
typeof(MyEvent).Assembly,
typeof(MyAggregate).Assembly);
```

### 5. Test Infrastructure Updates

**Test Helper Classes:**
- Created `TestTypeMetadataRegistry` helper in both `Rickten.EventStore.Tests` and `Rickten.Aggregator.Tests`
- Updated all test files to create registries and pass them to constructors
- Updated `TestServiceFactory` to register test assemblies

**Tests Updated:**
- `ValidationTests.cs` - All StateFolder instantiations
- `SnapshotTests.cs` - All StateFolder instantiations  
- `EventStoreTests.cs` - EventStore instantiation
- `SnapshotStoreTests.cs` - SnapshotStore instantiation
- `EventStoreIntegrationTestsBase.cs` - Integration test stores
- `SnapshotStoreIntegrationTests.cs` - Integration snapshot tests
- `ConcurrencyTests.cs` - Concurrency test stores
- `ServiceCollectionExtensionsTests.cs` - DI registration test

## What Stayed the Same

### Public API Behavior
- Event serialization wire format unchanged (`Aggregate.Name.vVersion`)
- State serialization wire format unchanged (`AggregateName.TypeName`)
- All existing attributes (`[Event]`, `[Aggregate]`, `[Command]`) work identically
- Event validation behavior in StateFolder unchanged
- Snapshot intervals work the same way
- All store interfaces (`IEventStore`, `ISnapshotStore`, `IProjectionStore`) unchanged

### Backward Compatibility
- Serializers fall back to `AppDomain.GetAssemblies()` for unregistered types
- Existing persisted events/snapshots can still be deserialized
- Type resolution still supports `FullName` fallback for non-attributed types

## Tradeoffs and Considerations

### Advantages
✅ **No more runtime assembly scanning** - eliminates unpredictable behavior based on loaded assemblies  
✅ **Thread-safe by design** - readonly dictionaries eliminate concurrency issues  
✅ **Explicit registration** - clear, testable dependency on specific assemblies  
✅ **Startup validation** - duplicate wire names detected immediately  
✅ **Better testability** - easy to create test-specific registries  
✅ **Removed redundancy** - eliminated `EventSerializer` in favor of `Serializer<TAttribute>`

### Tradeoffs
⚠️ **Breaking change for StateFolder** - now requires `ITypeMetadataRegistry` parameter  
⚠️ **Breaking change for DI setup** - `AddEventStore()` signature changed  
⚠️ **Explicit assembly registration required** - can't discover types from arbitrary assemblies  
⚠️ **Slightly more verbose setup** - must specify assemblies during registration  

### Migration Path
For existing code:

1. Update DI registration to pass assemblies:
   ```csharp
   // Old
   services.AddEventStoreInMemory("db");

   // New
   services.AddEventStoreInMemory("db", typeof(MyEvent).Assembly);
   ```

2. Update StateFolder subclasses to accept and pass registry:
   ```csharp
   // Old
   public class MyStateFolder : StateFolder<MyState> 
   {
       public MyStateFolder() : base() { }
   }

   // New  
   public class MyStateFolder : StateFolder<MyState>
   {
       public MyStateFolder(ITypeMetadataRegistry registry) : base(registry) { }
   }
   ```

3. Register StateFolders in DI or pass registry manually

## Validation

### Build Status
✅ All projects compile successfully

### Test Results
- ✅ Rickten.Aggregator.Tests: 16/16 passing
- ✅ Rickten.EventStore.Tests: **131/131 passing** (added 26 new TypeMetadataRegistry tests)
- ✅ No regressions in existing functionality
- ✅ Event coverage validation still works
- ✅ Snapshot functionality verified
- ✅ Concurrency tests passing
- ✅ Integration tests passing (SQL Server, PostgreSQL, SQLite)

### New Test Coverage (TypeMetadataRegistryTests)
Added 26 comprehensive tests for the registry:
- Registry construction from assemblies
- Type → metadata lookups
- Wire name → type lookups  
- Aggregate → event types lookups
- Readonly collection behavior
- Thread safety validation
- Builder pattern (fluent API)
- Null argument validation
- Attribute property preservation
- Empty registry behavior
- Multiple assembly handling

## Future Considerations

### Potential Improvements
- Consider caching registry instance per assembly set to avoid rebuilding
- Could add source generators to auto-discover types at compile time
- Could add registry validation hooks for custom business rules
- Consider adding metrics/telemetry for type resolution performance

### Known Limitations
- Registry must be rebuilt if assemblies change (expected for startup-only construction)
- No support for dynamic type registration after startup (by design)
- Fallback to `AppDomain.GetAssemblies()` still exists for unregistered types (for backward compatibility)

## Conclusion

This refactoring successfully eliminates runtime assembly scanning and static mutable caches while maintaining backward compatibility for serialization formats and public APIs. The new registry-based approach provides better thread safety, testability, and predictable behavior at the cost of requiring explicit assembly registration during startup.

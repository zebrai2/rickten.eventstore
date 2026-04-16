# Integration Test Refactoring Summary

## Overview
The integration tests have been refactored to use a **shared base class pattern** to eliminate code duplication and ensure consistency across all database providers.

## Structure

### Base Class: `EventStoreIntegrationTestsBase.cs`
This abstract class contains all common test methods that apply to every database provider:
- `ValueGeneratedOnAdd_AssignsSequentialIds`
- `UniqueConstraint_PreventsDuplicateVersions`
- `ConcurrentAppends_DatabaseConstraintEnforcement`
- `TransactionIsolation_PreservesConsistency`
- `IndexPerformance_GlobalPositionQueries`
- `StreamTypeFiltering_UsesIndex`
- `SnapshotCompositeKey_DatabaseEnforcement`
- `DefaultValueSql_CreatedAtTimestamp`
- `LargePayload_HandlesVarcharMax`

### Provider-Specific Classes
Each database provider inherits from the base class and provides:
1. **Database setup/teardown** (`IAsyncLifetime` or `IDisposable`)
2. **Aggregate type name** (unique per provider to avoid event type conflicts)
3. **Event factory methods** (create provider-specific event instances)
4. **Assertion helpers** (validate provider-specific event types)

#### SQL Server: `EventStoreIntegrationTestsSqlServer`
- Aggregate: `"ProductSqlServer"`
- Events: `ProductCreatedEventSqlServer`, `ProductUpdatedEventSqlServer`
- Container: SQL Server 2022 (Testcontainers)
- Requires: Docker Desktop

#### PostgreSQL: `EventStoreIntegrationTestsPostgres`
- Aggregate: `"ProductPostgres"`
- Events: `ProductCreatedEventPostgres`, `ProductUpdatedEventPostgres`
- Container: PostgreSQL 16 Alpine (Testcontainers)
- Requires: Docker Desktop

#### SQLite (In-Memory): `EventStoreIntegrationTestsSqlite`
- Aggregate: `"ProductSqlite"`
- Events: `ProductCreatedEventSqlite`, `ProductUpdatedEventSqlite`
- Provider: In-memory SQLite
- Requires: Nothing! Runs instantly without external dependencies

## Benefits

### 1. **Single Source of Truth**
When you add or modify a test in `EventStoreIntegrationTestsBase`, it automatically applies to all providers. For example:

```csharp
// Add this test once in the base class
[SkippableFact]
public async Task NewFeature_WorksCorrectly()
{
    SkipIfNotAvailable();
    var store = CreateEventStore();

    // Test logic using abstract methods
    var evt = CreateProductCreatedEvent("Test", 100m);
    await store.AppendAsync(...);

    // Validate using abstract assertion
    AssertProductCreatedEvent(loaded.Event, "Test", 100m);
}
```

This test will automatically run for SQL Server, PostgreSQL, and SQLite!

### 2. **No Event Type Conflicts**
Each provider uses a unique aggregate name:
- SQL Server: `[Event("ProductSqlServer", "Created", 1)]`
- PostgreSQL: `[Event("ProductPostgres", "Created", 1)]`

This prevents deserialization conflicts where the wrong event type is loaded.

### 3. **Consistent Testing**
All providers are tested with identical logic, ensuring feature parity and catching provider-specific bugs.

### 4. **Easy to Extend**
Adding a new database provider is simple:
1. Create a new class inheriting from `EventStoreIntegrationTestsBase`
2. Define unique event types with unique aggregate names
3. Implement the abstract members
4. Done! All tests run automatically.

## Abstract Members to Implement

```csharp
// Unique aggregate name for this provider's events
protected abstract string AggregateType { get; }

// Skip test if provider isn't available
protected abstract void SkipIfNotAvailable();

// Create database context
protected abstract EventStoreDbContext CreateContext();

// Factory methods for test events
protected abstract object CreateProductCreatedEvent(string name, decimal price);
protected abstract object CreateProductUpdatedEvent(decimal newPrice);

// Assertion helper
protected abstract void AssertProductCreatedEvent(object evt, string expectedName, decimal expectedPrice);
```

## Example: Adding a New Test

To add a test that runs on all providers, simply add it to `EventStoreIntegrationTestsBase.cs`:

```csharp
[SkippableFact]
public async Task Metadata_IsPersisted()
{
    SkipIfNotAvailable();
    var store = CreateEventStore();

    var metadata = new List<EventMetadata> 
    { 
        new EventMetadata("UserId", "user-123") 
    };

    await store.AppendAsync(
        new StreamPointer(new StreamIdentifier(AggregateType, "meta-test"), 0),
        new List<AppendEvent> 
        { 
            new AppendEvent(CreateProductCreatedEvent("Test", 50m), metadata) 
        });

    var loaded = new List<StreamEvent>();
    await foreach (var evt in store.LoadAsync(
        new StreamPointer(new StreamIdentifier(AggregateType, "meta-test"), 0)))
    {
        loaded.Add(evt);
    }

    Assert.Single(loaded);
    Assert.Contains(loaded[0].Metadata, m => m.Key == "UserId" && m.Value == "user-123");
}
```

This test will automatically run for all three database providers!

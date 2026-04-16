# Integration Test Coverage Matrix

## Test Coverage by Provider

All tests below are defined **once** in `EventStoreIntegrationTestsBase.cs` and automatically run for all three database providers:

| Test Name | SQL Server | PostgreSQL | SQLite | Description |
|-----------|------------|------------|--------|-------------|
| `ValueGeneratedOnAdd_AssignsSequentialIds` | ✅ | ✅ | ✅ | Verifies database auto-generates sequential IDs |
| `UniqueConstraint_PreventsDuplicateVersions` | ✅ | ✅ | ✅ | Verifies unique constraint prevents duplicate versions |
| `ConcurrentAppends_DatabaseConstraintEnforcement` | ✅ | ✅ | ✅ | Verifies only one concurrent append succeeds |
| `TransactionIsolation_PreservesConsistency` | ✅ | ✅ | ✅ | Verifies failed appends don't leave partial data |
| `IndexPerformance_GlobalPositionQueries` | ✅ | ✅ | ✅ | Verifies global position queries use indexes |
| `StreamTypeFiltering_UsesIndex` | ✅ | ✅ | ✅ | Verifies stream type filtering uses indexes |
| `SnapshotCompositeKey_DatabaseEnforcement` | ✅ | ✅ | ✅ | Verifies snapshot PK constraint works correctly |
| `DefaultValueSql_CreatedAtTimestamp` | ✅ | ✅ | ✅ | Verifies timestamp set by database |
| `LargePayload_HandlesVarcharMax` | ✅ | ✅ | ✅ | Verifies large payloads (10,000 chars) work |

**Total Coverage:** 9 tests × 3 providers = **27 test executions** from a single code definition!

## Provider Details

### 🗄️ SQL Server (Testcontainers)
```csharp
[Event("ProductSqlServer", "Created", 1)]
public record ProductCreatedEventSqlServer(string Name, decimal Price);
```
- **Setup:** Spins up SQL Server 2022 container via Docker
- **Performance:** ~5-10 seconds startup time
- **Benefits:** Tests production-like SQL Server behavior
- **Requirements:** Docker Desktop must be running

### 🐘 PostgreSQL (Testcontainers)
```csharp
[Event("ProductPostgres", "Created", 1)]
public record ProductCreatedEventPostgres(string Name, decimal Price);
```
- **Setup:** Spins up PostgreSQL 16 Alpine container via Docker
- **Performance:** ~5-10 seconds startup time
- **Benefits:** Tests PostgreSQL-specific features (JSONB, SERIAL)
- **Requirements:** Docker Desktop must be running

### ⚡ SQLite (In-Memory)
```csharp
[Event("ProductSqlite", "Created", 1)]
public record ProductCreatedEventSqlite(string Name, decimal Price);
```
- **Setup:** Instant - runs entirely in-memory
- **Performance:** <100ms total execution time
- **Benefits:** Fast feedback during development, no dependencies
- **Requirements:** None!

## Quick Start for Developers

### Run Fast Tests (SQLite Only)
```bash
dotnet test --filter "FullyQualifiedName~EventStoreIntegrationTestsSqlite"
```
**Result:** All 9 tests run in under 1 second ⚡

### Run All Integration Tests
```bash
dotnet test --filter "FullyQualifiedName~EventStoreIntegrationTests"
```
**Result:** All 27 tests run (requires Docker) 🐳

### Add a New Test
1. Open `EventStoreIntegrationTestsBase.cs`
2. Add your test method:
```csharp
[SkippableFact]
public async Task YourNewTest()
{
    SkipIfNotAvailable();
    var store = CreateEventStore();

    // Use abstract methods - works for all providers!
    await store.AppendAsync(
        new StreamPointer(new StreamIdentifier(AggregateType, "test"), 0),
        new List<AppendEvent> 
        { 
            new AppendEvent(CreateProductCreatedEvent("Test", 100m), null) 
        });
}
```
3. **Done!** Your test now runs on SQL Server, PostgreSQL, AND SQLite automatically! 🎉

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│     EventStoreIntegrationTestsBase (Abstract)           │
│  ┌───────────────────────────────────────────────────┐  │
│  │ • ValueGeneratedOnAdd_AssignsSequentialIds()      │  │
│  │ • UniqueConstraint_PreventsDuplicateVersions()    │  │
│  │ • ConcurrentAppends_DatabaseConstraintEnf...()   │  │
│  │ • TransactionIsolation_PreservesConsistency()     │  │
│  │ • IndexPerformance_GlobalPositionQueries()        │  │
│  │ • StreamTypeFiltering_UsesIndex()                 │  │
│  │ • SnapshotCompositeKey_DatabaseEnforcement()      │  │
│  │ • DefaultValueSql_CreatedAtTimestamp()            │  │
│  │ • LargePayload_HandlesVarcharMax()                │  │
│  └───────────────────────────────────────────────────┘  │
│                                                           │
│  Abstract Methods:                                        │
│  • string AggregateType { get; }                         │
│  • void SkipIfNotAvailable()                             │
│  • EventStoreDbContext CreateContext()                   │
│  • object CreateProductCreatedEvent(...)                 │
│  • object CreateProductUpdatedEvent(...)                 │
│  • void AssertProductCreatedEvent(...)                   │
└─────────────────────────────────────────────────────────┘
                            ▲
                            │ inherits
           ┌────────────────┼────────────────┐
           │                │                │
           │                │                │
┌──────────▼──────────┐ ┌───▼─────────────┐ ┌▼──────────────────┐
│ SQL Server Tests    │ │ PostgreSQL Tests│ │ SQLite Tests      │
│ (Testcontainers)    │ │ (Testcontainers)│ │ (In-Memory)       │
├─────────────────────┤ ├─────────────────┤ ├───────────────────┤
│ AggregateType:      │ │ AggregateType:  │ │ AggregateType:    │
│ "ProductSqlServer"  │ │ "ProductPostgres"│ │ "ProductSqlite"  │
│                     │ │                 │ │                   │
│ Events:             │ │ Events:         │ │ Events:           │
│ • ProductCreated... │ │ • ProductCreat..│ │ • ProductCreated..│
│ • ProductUpdated... │ │ • ProductUpdat..│ │ • ProductUpdated..│
│                     │ │                 │ │                   │
│ Container:          │ │ Container:      │ │ Provider:         │
│ SQL Server 2022     │ │ PostgreSQL 16   │ │ SQLite :memory:   │
│ 🐳 Docker required  │ │ 🐳 Docker req'd │ │ ⚡ No dependencies│
└─────────────────────┘ └─────────────────┘ └───────────────────┘
```

## Event Type Conflict Resolution

**Problem Solved:** Previously, all test classes used the same event names like `[Event("Product", "Created", 1)]`, causing deserialization conflicts where the wrong event type would be loaded.

**Solution:** Each provider now uses a unique aggregate name:
- SQL Server: `"ProductSqlServer"`
- PostgreSQL: `"ProductPostgres"`
- SQLite: `"ProductSqlite"`

This ensures events are correctly deserialized to their provider-specific types during tests.

## Troubleshooting

### Docker Tests Fail
**Issue:** `Docker is not available. Install Docker Desktop...`  
**Solution:** Install and start Docker Desktop, or run SQLite tests only

### Test Discovery Issues
**Issue:** Tests don't appear in Test Explorer  
**Solution:** Rebuild the solution (Ctrl+Shift+B)

### Event Type Conflicts
**Issue:** `Assert.IsType failed. Expected: ProductCreatedEventX, Actual: ProductCreatedEventY`  
**Solution:** This should no longer happen with unique aggregate names, but if it does:
1. Verify each provider has a unique `AggregateType` property
2. Verify event attributes use the unique aggregate name
3. Rebuild the solution

## Performance Metrics (Approximate)

| Provider | Startup | Per Test | Total (9 tests) |
|----------|---------|----------|-----------------|
| SQLite   | <10ms   | ~10ms    | ~100ms         |
| SQL Server| ~8s    | ~200ms   | ~10s           |
| PostgreSQL| ~6s    | ~200ms   | ~8s            |

**Recommendation:** Use SQLite tests during active development for instant feedback, then run full suite before committing.

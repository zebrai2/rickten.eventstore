# Integration Tests

> **🎯 NEW:** Tests now use a **shared base class pattern**! See [REFACTORING.md](REFACTORING.md) and [TEST-COVERAGE.md](TEST-COVERAGE.md) for details.

This directory contains comprehensive integration tests that validate the **real database behaviors** that the Rickten event store depends on. These tests go beyond the unit tests to ensure critical storage guarantees are actually enforced by the underlying database providers.

## Why Integration Tests Matter

The event store relies on specific relational database features that aren't properly tested with EF Core's InMemory provider:

1. **ValueGeneratedOnAdd()** - Auto-incrementing IDs for event global position
2. **Unique Constraints** - `(StreamType, StreamIdentifier, Version)` for optimistic concurrency
3. **Transaction Isolation** - Ensuring atomicity of batch operations
4. **Index Performance** - Validating query efficiency with real indexes

The InMemory provider **does not enforce** unique constraints, transactions, or other relational behaviors, making it unsuitable for testing these critical guarantees.

## 🚀 Quick Start

### Zero-Setup Approach (Just Works!)

Just run the tests! Docker containers automatically spin up and tear down:

```bash
# Make sure Docker Desktop is running, then:
dotnet test

# Or run specific database tests:
dotnet test --filter "FullyQualifiedName~SqlServer"
dotnet test --filter "FullyQualifiedName~Postgres"
dotnet test --filter "FullyQualifiedName~Sqlite"
```

**Prerequisites:**
- Docker Desktop installed and running (for SQL Server & PostgreSQL tests)
- No manual database setup or environment variables required!

**Note:** SQLite tests always run (no Docker needed). SQL Server and PostgreSQL tests auto-skip if Docker isn't available.

## Test Categories

### 🟢 EventStoreIntegrationTests.Sqlite.cs

SQLite-based integration tests that run in-memory (via shared connection) but use a **real relational database engine**.

**Key tests:**
- `ValueGeneratedOnAdd_AssignsSequentialIds` - Verifies auto-increment ID generation
- `UniqueConstraint_EnforcesOptimisticConcurrency` - Tests unique index enforcement
- `ConcurrentAppends_OnlyOneSucceeds` - Race condition handling
- `MultiStreamAppends_HaveCorrectGlobalOrdering` - Global position ordering
- `VersionConflict_PreservesDataIntegrity` - Transaction rollback on error
- `LoadAllAsync_FiltersCorrectlyWithRealData` - Index-based filtering

**Runs:** ✅ Always (no setup required)

### 🔵 EventStoreIntegrationTests.SqlServer.cs

**UPDATED!** SQL Server integration tests with **automatic Docker container management via Testcontainers**.

**Key tests:**
- `SqlServer_ValueGeneratedOnAdd_AssignsSequentialIdentityIds` - IDENTITY column behavior
- `SqlServer_ConcurrentAppends_DatabaseConstraintEnforcement` - Real constraint enforcement
- `SqlServer_TransactionIsolation_PreservesConsistency` - SQL Server transaction semantics
- `SqlServer_IndexPerformance_GlobalPositionQueries` - Index usage validation
- `SqlServer_DefaultValueSql_CreatedAtTimestamp` - GETUTCDATE() default value
- `SqlServer_LargePayload_HandlesVarcharMax` - Large payload handling
- `SqlServer_UniqueConstraint_PreventsDuplicateVersions` - Constraint enforcement
- `SqlServer_StreamTypeFiltering_UsesIndex` - Index-based filtering
- `SqlServer_SnapshotCompositeKey_DatabaseEnforcement` - PK constraint behavior

**Runs:** ✅ Automatically when Docker is available (skipped if Docker not running)

**No environment variables needed!** Just make sure Docker Desktop is running.

### 🐘 EventStoreIntegrationTests.Postgres.cs

**NEW!** PostgreSQL integration tests with automatic Docker container management.

**Key tests:**
- `Postgres_SerialSequence_AssignsSequentialIds` - PostgreSQL SERIAL/BIGSERIAL behavior
- `Postgres_ConcurrentAppends_DatabaseConstraintEnforcement` - Constraint enforcement
- `Postgres_TransactionIsolation_PreservesConsistency` - PostgreSQL MVCC semantics
- `Postgres_SnapshotCompositeKey_DatabaseEnforcement` - PK constraint behavior
- `Postgres_JsonbPayload_HandlesComplexData` - JSONB storage validation
- `Postgres_IndexPerformance_GlobalPositionQueries` - Index usage

**Runs:** ✅ Automatically when Docker is available (skipped if Docker not running)

## Database-Specific Behaviors Tested

### SQL Server
- ✅ IDENTITY columns for auto-increment
- ✅ Unique index enforcement on concurrent operations
- ✅ GETUTCDATE() for timestamp defaults
- ✅ VARCHAR(MAX) for large payloads
- ✅ Transaction isolation levels

### PostgreSQL
- ✅ SERIAL/BIGSERIAL sequences for auto-increment
- ✅ Unique constraint enforcement
- ✅ NOW() for timestamp defaults
- ✅ TEXT/JSONB for payloads
- ✅ MVCC transaction isolation

### SQLite
- ✅ AUTOINCREMENT for sequential IDs
- ✅ Unique constraint enforcement (even on in-memory databases)
- ✅ Transaction rollback on constraint violations
- ✅ Index-based query optimization

## Running Tests

```bash
# Run all integration tests
dotnet test --filter Category=Integration

# Run only SQLite tests (always work)
dotnet test --filter FullyQualifiedName~Sqlite

# Run Testcontainers tests (auto-managed Docker)
dotnet test --filter "FullyQualifiedName~Testcontainers|FullyQualifiedName~Postgres"

# Run only manual SQL Server tests
dotnet test --filter FullyQualifiedName~SqlServer&FullyQualifiedName!~Testcontainers

# Run all tests (will skip unavailable databases)
dotnet test
```

## CI/CD Configuration

### GitHub Actions Example

```yaml
- name: Start test databases
  run: docker-compose -f docker-compose.tests.yml up -d

- name: Wait for databases
  run: |
    docker-compose -f docker-compose.tests.yml exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "YourStrong@Passw0rd" -C -Q "SELECT 1"
    docker-compose -f docker-compose.tests.yml exec -T postgres pg_isready -U postgres

- name: Run tests
  env:
    RICKTEN_SQLSERVER_CONNECTION_STRING: "Server=localhost,1433;Database=RicktenEventStoreTests;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True"
    RICKTEN_POSTGRES_CONNECTION_STRING: "Host=localhost;Port=5432;Database=rickten_eventstore_tests;Username=postgres;Password=postgres"
  run: dotnet test

- name: Cleanup
  if: always()
  run: docker-compose -f docker-compose.tests.yml down -v
```

Or just let Testcontainers handle everything:

```yaml
- name: Run tests with Testcontainers
  run: dotnet test
```

## Troubleshooting

### Tests are skipped
- **Testcontainers tests**: Make sure Docker Desktop is installed and running
- **Manual SQL Server tests**: Set `RICKTEN_SQLSERVER_CONNECTION_STRING` environment variable
- **Manual Postgres tests**: Set `RICKTEN_POSTGRES_CONNECTION_STRING` environment variable

### Docker issues
```bash
# Check Docker is running
docker info

# Pull required images
docker pull mcr.microsoft.com/mssql/server:2022-latest
docker pull postgres:16-alpine

# Check running containers
docker ps
```

### Port conflicts
If ports 1433 or 5432 are already in use, edit `docker-compose.tests.yml` to use different ports.

## Benefits of This Approach

✅ **Zero friction for new developers** - Testcontainers tests work out of the box  
✅ **Multi-database validation** - Ensures portability across SQL Server, PostgreSQL, and SQLite  
✅ **Real constraint enforcement** - Tests actual database behavior, not mocks  
✅ **CI/CD ready** - Docker Compose or Testcontainers work in pipelines  
✅ **Fast feedback** - Containers start in seconds  
✅ **Isolated tests** - Each test run gets fresh databases  

## Learn More

- [Testcontainers for .NET](https://dotnet.testcontainers.org/)
- [EF Core Database Providers](https://learn.microsoft.com/en-us/ef/core/providers/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)

# CI/CD - set as secret/environment variable in your pipeline
```

Tests automatically create a uniquely named database and clean it up after completion.

### SnapshotStoreIntegrationTests.cs

Comprehensive snapshot functionality tests with real database.

**Key tests:**
- `SnapshotRestore_LoadsFromSnapshotThenAppliesNewEvents` - Core snapshot restore pattern
- `SnapshotUpdate_OverwritesPreviousSnapshot` - Snapshot replacement
- `MultipleStreams_IndependentSnapshots` - Snapshot isolation
- `SnapshotRestore_CompleteEndToEndScenario` - Full lifecycle test
- `SnapshotCompositeKey_EnforcesUniqueness` - Primary key enforcement

**Runs:** Always (SQLite)

### ConcurrencyTests.cs

Focused tests on optimistic concurrency control and race conditions.

**Key tests:**
- `OptimisticConcurrency_DetectsVersionConflict` - Classic OCC scenario
- `OptimisticConcurrency_Client2CanRetry` - Retry pattern after conflict
- `RaceCondition_MultipleThreads_OnlyOneWins` - Multi-threaded race conditions
- `BatchAppend_AllOrNothing_OnVersionConflict` - Transaction atomicity
- `IsolatedStreams_NoCrossStreamInterference` - Stream isolation
- `StaleRead_DetectedOnWrite` - Stale read detection
- `UniqueConstraint_EnforcedAtDatabaseLevel` - Database-level constraint enforcement

**Runs:** Always (SQLite)

## Running the Tests

### All Tests (SQLite only)
```bash
dotnet test
```

### Including SQL Server Tests
```powershell
# Set connection string
$env:RICKTEN_SQLSERVER_CONNECTION_STRING = "Server=localhost;Database=RicktenEventStoreTests;Trusted_Connection=True;TrustServerCertificate=True"

# Run tests
dotnet test
```

### Specific Test Category
```bash
dotnet test --filter "FullyQualifiedName~EventStoreIntegrationTestsSqlite"
dotnet test --filter "FullyQualifiedName~ConcurrencyTests"
```

## CI/CD Integration

### GitHub Actions Example
```yaml
- name: Run Integration Tests (SQLite)
  run: dotnet test --filter "FullyQualifiedName~Integration" --no-build

- name: Run SQL Server Integration Tests
  env:
    RICKTEN_SQLSERVER_CONNECTION_STRING: ${{ secrets.SQL_SERVER_CONNECTION }}
  run: dotnet test --filter "FullyQualifiedName~SqlServer" --no-build
  if: env.RICKTEN_SQLSERVER_CONNECTION_STRING != ''
```

## What These Tests Validate

### 1. Optimistic Concurrency Control
The unique constraint on `(StreamType, StreamIdentifier, Version)` is the foundation of optimistic concurrency. These tests verify:
- Constraint is actually enforced by the database
- Concurrent appends at the same version fail (except one)
- Version conflicts are detected and reported correctly
- Retry logic works with actual version conflicts

### 2. Global Position Ordering
The auto-incrementing `Id` column serves as the global position. Tests verify:
- IDs are generated sequentially by the database
- Global position is monotonically increasing
- Multi-stream events maintain global order
- LoadAllAsync returns events in correct order

### 3. Snapshot Restore Pattern
Snapshots enable efficient aggregate rebuilding. Tests verify:
- Snapshots save and load correctly
- Loading events after snapshot version works
- Snapshot updates replace old snapshots (composite PK)
- Multiple streams have independent snapshots

### 4. Transaction Semantics
Database transactions ensure data integrity. Tests verify:
- Failed appends don't leave partial data
- Version conflicts roll back entire batch
- Committed data is visible to other connections
- Database remains consistent after errors

## Test Data Isolation

Each test uses isolated data:
- **SQLite**: Fresh in-memory database per test class (via IDisposable)
- **SQL Server**: Uniquely named database per test class, cleaned up after tests
- Tests don't interfere with each other

## Performance Considerations

Integration tests are slower than unit tests because they:
- Use real database engines
- Perform actual I/O operations
- Execute database constraints and indexes

For SQL Server tests:
- Database creation/deletion adds overhead
- Consider running in CI/CD only or on-demand

## Future Enhancements

Potential additions:
- **PostgreSQL integration tests** - Add Npgsql provider tests
- **Projection integration tests** - Validate projection position tracking
- **Migration tests** - Test database schema migrations
- **Performance benchmarks** - Measure throughput under load
- **Deadlock scenarios** - Test concurrent transactions

## Contributing

When adding new event store features, ensure integration tests cover:
1. Database constraint enforcement
2. Transaction behavior
3. Concurrency scenarios
4. Index usage and performance

Prefer integration tests over InMemory tests for anything related to:
- Unique constraints
- Auto-increment IDs
- Transactions
- Database-specific features

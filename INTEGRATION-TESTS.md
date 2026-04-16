# 🐳 Integration Tests with Docker - Quick Start

**Zero setup! Just run the tests** - containers spin up automatically:

## How to Run

```powershell
# Make sure Docker Desktop is running, then just:
dotnet test

# That's it! Testcontainers will:
# ✅ Pull SQL Server & PostgreSQL images (first time only)
# ✅ Start fresh containers  
# ✅ Run your tests
# ✅ Clean up automatically
```

## What's Included 🎉

### All Tests Use Testcontainers (Auto-Managed Docker)

| Database | Tests | Setup Required |
|----------|-------|----------------|
| **SQLite** | ✅ 6 tests | None (always works) |
| **SQL Server** | ✅ 9 tests | Docker Desktop running |
| **PostgreSQL** | ✅ 7 tests | Docker Desktop running |

**Total: 22 integration tests** across 3 database providers!

## Run Specific Databases

```powershell
# Run only SQL Server tests
dotnet test --filter "FullyQualifiedName~SqlServer"

# Run only PostgreSQL tests
dotnet test --filter "FullyQualifiedName~Postgres"

# Run only SQLite tests (no Docker needed)
dotnet test --filter "FullyQualifiedName~Sqlite"

# Run all tests
dotnet test
```

## Why This Rocks 🎸

✅ **No environment setup hassle** - Just Docker Desktop  
✅ **Fresh databases every run** - No test pollution  
✅ **Multi-database validation** - Catch provider-specific issues  
✅ **CI/CD ready** - Works in GitHub Actions, Azure DevOps, etc.  
✅ **Fast** - Containers start in seconds  

## Need Help?

Check the [Integration README](Rickten.EventStore.Tests/Integration/README.md) for detailed docs.

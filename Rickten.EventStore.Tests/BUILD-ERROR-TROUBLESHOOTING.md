# 🔧 Build Error Troubleshooting Guide

## If You're Seeing Build Errors After the Refactoring

### Step 1: Clean and Rebuild

Run these commands in PowerShell from the solution root:

```powershell
# Clean all build outputs
dotnet clean

# Restore NuGet packages
dotnet restore

# Rebuild everything
dotnet build
```

### Step 2: Check for Specific Errors

Run the build and look at the specific errors:

```powershell
dotnet build 2>&1 | Out-File build-errors.txt
notepad build-errors.txt
```

### Common Issues & Fixes

#### Issue 1: "The type or namespace name 'SqliteConnection' could not be found"

**Fix:** Add using statement (already done in EventStoreDbContextTests.cs):
```csharp
using Microsoft.Data.Sqlite;
```

#### Issue 2: "The name 'UseSqlite' does not exist"

**Fix:** The package is already referenced in the .csproj. Try:
```powershell
dotnet restore
dotnet build
```

#### Issue 3: "Package Microsoft.EntityFrameworkCore.InMemory is not used"

**Fix:** You can optionally remove this package since we're no longer using it:
```powershell
dotnet remove Rickten.EventStore.Tests package Microsoft.EntityFrameworkCore.InMemory
```

#### Issue 4: Testcontainers build errors

**Fix:** Make sure Docker Desktop is installed (tests will skip if not available, but packages still need to compile).

### Step 3: Visual Studio Specific

If using Visual Studio:

1. **Close Visual Studio**
2. Delete all `bin` and `obj` folders:
   ```powershell
   Get-ChildItem -Path . -Include bin,obj -Recurse | Remove-Item -Recurse -Force
   ```
3. **Reopen Visual Studio**
4. **Rebuild Solution** (Ctrl+Shift+B)

### Step 4: Check Package Versions

All packages should be version 10.0.x for .NET 10:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.5" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.5" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
```

### Step 5: Verify Changes

Make sure these files have the correct content:

#### ✅ EventStoreDbContextTests.cs should have:
```csharp
using Microsoft.Data.Sqlite;  // This line at top

private (SqliteConnection Connection, DbContextOptions<EventStoreDbContext> Options) CreateOptions()
{
    var connection = new SqliteConnection("DataSource=:memory:");
    connection.Open();
    // ...
}
```

#### ✅ Integration tests should have unique aggregate names:
- `EventStoreIntegrationTestsSqlite` → `"ProductSqlite"`
- `EventStoreIntegrationTestsSqlServer` → `"ProductSqlServer"`  
- `EventStoreIntegrationTestsPostgres` → `"ProductPostgres"`

### Step 6: Run Tests

Once build succeeds:

```powershell
# Run just the DbContext tests
dotnet test --filter "EventStoreDbContextTests"

# Run all tests
dotnet test
```

### Still Having Issues?

Please provide the actual error messages from the build output. Common scenarios:

1. **Copy the errors from Error List** in Visual Studio
2. **Or run:** `dotnet build` and copy the error messages
3. **Share specific errors** so we can fix them precisely

## Quick Reset (Nuclear Option)

If all else fails:

```powershell
# From solution root
git status  # Make sure your work is committed
git clean -fdx  # Remove all untracked files (careful!)
dotnet restore
dotnet build
```

⚠️ **Warning:** `git clean -fdx` will delete all files not tracked by git!

# SQL Server Integration Test Setup Guide

This guide helps you set up SQL Server integration tests for local development and CI/CD.

## Prerequisites

You need **one** of the following:
1. SQL Server LocalDB (included with Visual Studio)
2. SQL Server Express (free)
3. SQL Server Developer Edition (free)
4. SQL Server (any edition) running locally or remotely
5. Azure SQL Database

## Option 1: SQL Server LocalDB (Recommended for Local Development)

LocalDB is automatically installed with Visual Studio and is perfect for development.

### Setup
```powershell
# Check if LocalDB is installed
sqllocaldb info

# Start the default instance (if not running)
sqllocaldb start mssqllocaldb

# Set environment variable for tests
$env:RICKTEN_SQLSERVER_CONNECTION_STRING = "Server=(localdb)\mssqllocaldb;Database=RicktenEventStoreTests;Trusted_Connection=True;TrustServerCertificate=True"

# Run tests
dotnet test --filter "FullyQualifiedName~SqlServer"
```

### Make Permanent (PowerShell Profile)
```powershell
# Add to your PowerShell profile (~\Documents\PowerShell\Microsoft.PowerShell_profile.ps1)
$env:RICKTEN_SQLSERVER_CONNECTION_STRING = "Server=(localdb)\mssqllocaldb;Database=RicktenEventStoreTests;Trusted_Connection=True;TrustServerCertificate=True"
```

## Option 2: SQL Server Express

### Install
Download from: https://www.microsoft.com/en-us/sql-server/sql-server-downloads

### Setup
```powershell
# Default connection string for SQL Server Express
$env:RICKTEN_SQLSERVER_CONNECTION_STRING = "Server=localhost\SQLEXPRESS;Database=RicktenEventStoreTests;Trusted_Connection=True;TrustServerCertificate=True"

# Run tests
dotnet test --filter "FullyQualifiedName~SqlServer"
```

## Option 3: SQL Server in Docker

### Setup
```bash
# Pull SQL Server image
docker pull mcr.microsoft.com/mssql/server:2022-latest

# Run SQL Server container
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong@Passw0rd" \
  -p 1433:1433 --name sql-server-test \
  -d mcr.microsoft.com/mssql/server:2022-latest

# Set environment variable
$env:RICKTEN_SQLSERVER_CONNECTION_STRING = "Server=localhost,1433;Database=RicktenEventStoreTests;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True"

# Run tests
dotnet test --filter "FullyQualifiedName~SqlServer"
```

## Option 4: Azure SQL Database

### Setup in Azure Portal
1. Create Azure SQL Database
2. Configure firewall to allow your IP
3. Get connection string from portal

### Setup Locally
```powershell
$env:RICKTEN_SQLSERVER_CONNECTION_STRING = "Server=tcp:yourserver.database.windows.net,1433;Database=RicktenEventStoreTests;User Id=yourusername;Password=yourpassword;Encrypt=True;TrustServerCertificate=False;"

# Run tests
dotnet test --filter "FullyQualifiedName~SqlServer"
```

## CI/CD Setup

### GitHub Actions

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore

    # SQLite integration tests (always run)
    - name: Run SQLite Integration Tests
      run: dotnet test --filter "FullyQualifiedName~Integration&FullyQualifiedName!~SqlServer" --no-build

    # SQL Server integration tests (using LocalDB on Windows runners)
    - name: Run SQL Server Integration Tests
      env:
        RICKTEN_SQLSERVER_CONNECTION_STRING: "Server=(localdb)\\mssqllocaldb;Database=RicktenEventStoreTests;Trusted_Connection=True;TrustServerCertificate=True"
      run: dotnet test --filter "FullyQualifiedName~SqlServer" --no-build
```

### GitHub Actions with SQL Server Service Container

```yaml
name: Integration Tests with SQL Server

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest

    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: Y
          MSSQL_SA_PASSWORD: YourStrong@Passw0rd
        ports:
          - 1433:1433
        options: >-
          --health-cmd="/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P YourStrong@Passw0rd -Q 'SELECT 1'"
          --health-interval=10s
          --health-timeout=5s
          --health-retries=5

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'

    - name: Run All Tests
      env:
        RICKTEN_SQLSERVER_CONNECTION_STRING: "Server=localhost,1433;Database=RicktenEventStoreTests;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True"
      run: dotnet test
```

### Azure DevOps

```yaml
trigger:
- main

pool:
  vmImage: 'windows-latest'

variables:
  RICKTEN_SQLSERVER_CONNECTION_STRING: 'Server=(localdb)\mssqllocaldb;Database=RicktenEventStoreTests;Trusted_Connection=True;TrustServerCertificate=True'

steps:
- task: UseDotNet@2
  inputs:
    version: '10.0.x'

- task: DotNetCoreCLI@2
  displayName: 'Restore'
  inputs:
    command: 'restore'

- task: DotNetCoreCLI@2
  displayName: 'Build'
  inputs:
    command: 'build'
    arguments: '--no-restore'

- task: DotNetCoreCLI@2
  displayName: 'Run Integration Tests'
  inputs:
    command: 'test'
    arguments: '--no-build'
  env:
    RICKTEN_SQLSERVER_CONNECTION_STRING: $(RICKTEN_SQLSERVER_CONNECTION_STRING)
```

## Troubleshooting

### Tests Skip with "SQL Server connection string not configured"
**Solution:** Set the `RICKTEN_SQLSERVER_CONNECTION_STRING` environment variable

```powershell
# Check if variable is set
$env:RICKTEN_SQLSERVER_CONNECTION_STRING

# Set it if empty
$env:RICKTEN_SQLSERVER_CONNECTION_STRING = "Server=(localdb)\mssqllocaldb;Database=RicktenEventStoreTests;Trusted_Connection=True;TrustServerCertificate=True"
```

### Connection Timeout
**Solution:** Ensure SQL Server is running

```powershell
# For LocalDB
sqllocaldb start mssqllocaldb

# For SQL Server service
net start MSSQLSERVER
# or
net start MSSQL$SQLEXPRESS
```

### Login Failed
**Solution:** Check credentials and connection string

```powershell
# Test connection with sqlcmd
sqlcmd -S "(localdb)\mssqllocaldb" -E -Q "SELECT @@VERSION"

# For SQL Server Express
sqlcmd -S "localhost\SQLEXPRESS" -E -Q "SELECT @@VERSION"
```

### Database Already Exists
**Solution:** Tests auto-create/delete databases with unique names, but if cleanup fails:

```sql
-- Connect to master database and drop test databases
USE master;
GO

-- Find test databases
SELECT name FROM sys.databases WHERE name LIKE 'RicktenEventStoreTests_%';
GO

-- Drop them
DROP DATABASE IF EXISTS RicktenEventStoreTests_abc123;
GO
```

### TrustServerCertificate Error
**Solution:** Add `TrustServerCertificate=True` to connection string

```
Server=localhost;Database=RicktenEventStoreTests;Trusted_Connection=True;TrustServerCertificate=True
```

## Performance Notes

- **LocalDB**: Slower than full SQL Server but sufficient for testing
- **SQL Server Express**: Good performance, free, suitable for CI/CD
- **Docker**: Adds container overhead but provides isolation
- **Azure SQL**: Cloud latency may slow tests; use for production-like validation

## Security Notes

### Local Development
- Use Windows Authentication (`Trusted_Connection=True`) when possible
- Avoid hardcoding passwords in scripts

### CI/CD
- Store connection strings as secrets
- Use managed identities for Azure resources
- Rotate credentials regularly

### Connection String Best Practices
```powershell
# Good: Use environment variable
$env:RICKTEN_SQLSERVER_CONNECTION_STRING = "Server=localhost;..."

# Bad: Hardcode in scripts or commit to source control
# DO NOT DO THIS
```

## Cleanup

The tests automatically create and delete databases, but you can manually cleanup:

```sql
-- List test databases
SELECT name FROM sys.databases WHERE name LIKE 'RicktenEventStoreTests_%';

-- Clean up all test databases
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += 'DROP DATABASE IF EXISTS ' + QUOTENAME(name) + ';' + CHAR(13)
FROM sys.databases 
WHERE name LIKE 'RicktenEventStoreTests_%';
EXEC sp_executesql @sql;
```

## FAQ

**Q: Do I need SQL Server to run all tests?**
A: No. SQLite integration tests (24 tests) run without SQL Server. Only the 9 SQL Server-specific tests require it.

**Q: Can I use PostgreSQL instead?**
A: Not currently, but you can create similar tests for PostgreSQL. See `Integration\README.md` for future enhancements.

**Q: Why are SQL Server tests separate?**
A: To validate SQL Server-specific behaviors (IDENTITY columns, GETUTCDATE(), varchar(max), etc.) that differ from SQLite.

**Q: Will tests interfere with my existing databases?**
A: No. Tests create uniquely named databases (e.g., `RicktenEventStoreTests_abc123def456...`) and clean them up automatically.

**Q: How long do SQL Server tests take?**
A: ~1-2 seconds total. Database creation/deletion is the main overhead.

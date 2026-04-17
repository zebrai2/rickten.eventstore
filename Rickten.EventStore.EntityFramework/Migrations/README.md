# Database Migrations

This folder contains Entity Framework Core migrations for the Rickten Event Store.

## Migration: AddProjectionNamespace (20250101000000)

**Purpose**: Adds namespace support to the Projections table, allowing the same projection store to be used for both public projections (`"system"` namespace) and reaction-private projections (`"reaction"` namespace).

**Changes**:
1. Adds `Namespace` column (nvarchar(255), NOT NULL, default: `"system"`)
2. Changes primary key from `ProjectionKey` to composite key `(Namespace, ProjectionKey)`
3. Sets all existing projection records to `"system"` namespace

**Upgrade Path**:
- All existing projections will automatically be migrated to the `"system"` namespace
- No action required for existing data
- Reactions will use the `"reaction"` namespace for their private projections

**Breaking Changes**: 
- None for application code (defaults handle backward compatibility)
- Database schema change requires migration execution

## Applying Migrations

### Using dotnet CLI:
```bash
dotnet ef database update --project Rickten.EventStore.EntityFramework
```

### Using Package Manager Console (Visual Studio):
```powershell
Update-Database -Project Rickten.EventStore.EntityFramework
```

### Manual SQL Script:
```bash
dotnet ef migrations script --project Rickten.EventStore.EntityFramework --output migration.sql
```

## Database Provider Compatibility

The migration uses generic SQL commands compatible with:
- SQL Server
- PostgreSQL
- SQLite
- MySQL

Column types are automatically adapted by EF Core based on the configured provider.

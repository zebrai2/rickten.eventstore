# Database Migrations

This folder contains Entity Framework Core migrations for the Rickten Event Store.

## Migration: InitialCreate (20260521161153)

**Purpose**: Creates the initial database schema for the Event Store with tables for events, snapshots, and projections.

**Changes**:
1. Creates `Events` table with columns:
   - `Id` (bigint, auto-increment) - Global position/sequence
   - `StreamType` (nvarchar(255)) - The type of aggregate
   - `StreamIdentifier` (nvarchar(255)) - The aggregate instance ID
   - `Version` (bigint) - Event version in the stream
   - `EventType` (nvarchar(255)) - The type of the event
   - `EventData` (nvarchar(max)) - JSON serialized event data
   - `Metadata` (nvarchar(max)) - JSON serialized metadata
   - `CreatedAt` (datetime2) - Timestamp (defaults to UTC now)
   - Indexes:
     - Primary key on `Id`
     - Unique index on `(StreamType, StreamIdentifier, Version)` for optimistic concurrency
     - Index on `(StreamType, StreamIdentifier)` for stream queries
     - Index on `Id` for global position queries

2. Creates `Snapshots` table with columns:
   - `StreamType` (nvarchar(255))
   - `StreamIdentifier` (nvarchar(255))
   - `Version` (bigint) - Snapshot version
   - `StateType` (nvarchar(max)) - CLR type of the state
   - `State` (nvarchar(max)) - JSON serialized state
   - `CreatedAt` (datetime2) - Timestamp (defaults to UTC now)
   - Composite primary key on `(StreamType, StreamIdentifier)`

3. Creates `Projections` table with columns:
   - `Namespace` (nvarchar(255)) - Namespace for multi-tenancy/bounded contexts
   - `ProjectionKey` (nvarchar(255)) - Unique key for the projection
   - `GlobalPosition` (bigint) - Last processed event position
   - `StateType` (nvarchar(255)) - CLR type of the projection state
   - `State` (nvarchar(max)) - JSON serialized projection state
   - `UpdatedAt` (datetime2) - Last update timestamp (defaults to UTC now)
   - Composite primary key on `(Namespace, ProjectionKey)`

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

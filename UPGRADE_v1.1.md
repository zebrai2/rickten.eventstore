# Rickten Event Store v1.1 Upgrade Guide

## Overview

Version 1.1 introduces **Rickten.Reactor** and enhances the projection storage system with namespace support. This allows reactions to maintain their own private projection state while sharing the same database with public projections.

## What's New

### 1. Rickten.Reactor Package
- New mechanism package for event-driven command execution (events → commands)
- Completes the Rickten triangle: Aggregator (commands → events), Projector (events → read models), Reactor (events → commands)
- Supports projection-based stream selection for one-to-many command execution

### 2. Namespace Support in IProjectionStore
- `IProjectionStore.LoadProjectionAsync` and `SaveProjectionAsync` now accept a `namespace` parameter
- Default namespace: `"system"` (for public projections)
- Reactions use `"reaction"` namespace (for private projection state)
- Enables sharing the same database/repository for all projections

## Breaking Changes

### API Changes
**IProjectionStore Interface**:
```csharp
// OLD
Task<Projection<TState>?> LoadProjectionAsync<TState>(
    string projectionKey,
    CancellationToken cancellationToken = default);

Task SaveProjectionAsync<TState>(
    string projectionKey,
    long globalPosition,
    TState state,
    CancellationToken cancellationToken = default);

// NEW
Task<Projection<TState>?> LoadProjectionAsync<TState>(
    string projectionKey,
    string @namespace = "system",  // NEW parameter with default
    CancellationToken cancellationToken = default);

Task SaveProjectionAsync<TState>(
    string projectionKey,
    long globalPosition,
    TState state,
    string @namespace = "system",  // NEW parameter with default
    CancellationToken cancellationToken = default);
```

**Impact**: 
- Existing code will continue to work due to default parameter values
- No code changes required for existing projections
- Custom `IProjectionStore` implementations must be updated

### Database Schema Changes

**Projections Table**:
- New column: `Namespace` (nvarchar(255), NOT NULL)
- Primary key changed from `ProjectionKey` to composite `(Namespace, ProjectionKey)`

## Upgrade Steps

### Step 1: Update NuGet Packages
```bash
dotnet add package Rickten.EventStore --version 1.1.0
dotnet add package Rickten.EventStore.EntityFramework --version 1.1.0
dotnet add package Rickten.Projector --version 1.1.0
dotnet add package Rickten.Reactor --version 1.1.0  # NEW
```

### Step 2: Apply Database Migration

**Option A: Using dotnet CLI**
```bash
cd your-project-directory
dotnet ef database update --project path/to/Rickten.EventStore.EntityFramework
```

**Option B: Using Package Manager Console (Visual Studio)**
```powershell
Update-Database -Project Rickten.EventStore.EntityFramework
```

**Option C: Generate SQL Script for Manual Application**
```bash
dotnet ef migrations script --project Rickten.EventStore.EntityFramework --output migration.sql
# Review and apply migration.sql to your database
```

### Step 3: Update Custom IProjectionStore Implementations (if any)

If you have custom implementations of `IProjectionStore`, update them to include the namespace parameter:

```csharp
public class MyCustomProjectionStore : IProjectionStore
{
    public async Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        string @namespace = "system",  // Add this parameter
        CancellationToken cancellationToken = default)
    {
        // Update implementation to filter by namespace
        // Example: WHERE ProjectionKey = @key AND Namespace = @namespace
    }

    public async Task SaveProjectionAsync<TState>(
        string projectionKey,
        long globalPosition,
        TState state,
        string @namespace = "system",  // Add this parameter
        CancellationToken cancellationToken = default)
    {
        // Update implementation to include namespace
        // Example: INSERT/UPDATE with Namespace = @namespace
    }
}
```

### Step 4: Verify Upgrade

Run your existing tests to ensure projections continue to work:
```bash
dotnet test
```

All existing projections should continue to work with the `"system"` namespace.

## Using Rickten.Reactor (Optional)

If you want to use the new Reactor functionality:

### 1. Define a Reaction

```csharp
using Rickten.Reactor;
using Rickten.Projector;

// Define a projection for stream selection
[Projection("MyProjection")]
public class MyProjection : Projection<MyView>
{
    public override MyView InitialView() => new MyView();

    protected override MyView ApplyEvent(MyView view, StreamEvent streamEvent)
    {
        // Build view state
        return view;
    }
}

// Define a reaction
[Reaction("MyReaction", EventTypes = new[] { "Aggregate.EventName.v1" })]
public class MyReaction : Reaction<MyView, MyCommand>
{
    private readonly MyProjection _projection = new();

    public MyReaction(ITypeMetadataRegistry registry) : base(registry) { }

    public override IProjection<MyView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(MyView view, StreamEvent trigger)
    {
        // Use projection view to determine which streams need commands
        yield return new StreamIdentifier("TargetAggregate", "some-id");
    }

    protected override MyCommand BuildCommand(StreamIdentifier stream, MyView view, StreamEvent trigger)
    {
        return new MyCommand(stream.Identifier);
    }
}
```

### 2. Register and Execute

```csharp
// Register reaction
registry.Register<MyReaction>();

// Execute reaction
await ReactionRunner.CatchUpAsync(
    eventStore,
    projectionStore,  // Same store used by public projections
    reaction,
    folder,
    decider);
```

## Rollback Procedure

If you need to rollback to the previous version:

### 1. Revert Database Migration
```bash
dotnet ef database update PreviousMigration --project Rickten.EventStore.EntityFramework
```

Or apply the Down migration manually:
```sql
-- Drop composite primary key
ALTER TABLE Projections DROP CONSTRAINT PK_Projections;

-- Recreate original primary key
ALTER TABLE Projections ADD CONSTRAINT PK_Projections PRIMARY KEY (ProjectionKey);

-- Drop Namespace column
ALTER TABLE Projections DROP COLUMN Namespace;
```

### 2. Downgrade NuGet Packages
```bash
dotnet add package Rickten.EventStore --version 1.0.0
dotnet add package Rickten.EventStore.EntityFramework --version 1.0.0
dotnet add package Rickten.Projector --version 1.0.0
```

## Support

For issues or questions:
- GitHub Issues: https://github.com/zebrai2/rickten.eventstore/issues
- Documentation: See package README files

## Compatibility

- **Minimum .NET Version**: .NET 10.0
- **Database Providers**: SQL Server, PostgreSQL, SQLite, MySQL
- **Breaking Changes**: Custom `IProjectionStore` implementations require updates

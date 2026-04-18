# Rickten v1.1.0 Release Notes

**Release Date**: TBD  
**Repository**: https://github.com/zebrai2/rickten.eventstore

## Overview

Rickten 1.1 introduces **Rickten.Reactor** and **projection namespaces**.

Most applications using the official Entity Framework projection store can upgrade without changing call sites. Custom `IProjectionStore` implementations must be updated to support the new namespace-aware projection storage contract.

### Compatibility Summary

- **Application-level compatible** for standard usage
- **Custom store implementers must update** (interface change)

This is a feature release with a clear implementer note.

## What's New

### 🎉 New Package: Rickten.Reactor

Event-driven command execution mechanism that completes the Rickten architecture:
- **Aggregator**: Commands → Events
- **Projector**: Events → Read Models  
- **Reactor**: Events → Commands *(NEW!)*

**Key Features**:
- Projection-based stream selection for one-to-many reactions
- Dual-checkpoint model (trigger position + projection position)
- Two-checkpoint recovery with automatic projection rebuild
- TypeMetadataRegistry integration for validation
- Optional diagnostic logging

**Example**:
```csharp
[Reaction("MembershipDefinitionChanged", EventTypes = new[] { "MembershipDefinition.Changed.v1" })]
public class MembershipReaction : Reaction<MembershipView, RecalculateCommand>
{
    private readonly MembershipProjection _projection = new();

    public MembershipReaction(ITypeMetadataRegistry registry) : base(registry) { }

    public override IProjection<MembershipView> Projection => _projection;

    protected override IEnumerable<StreamIdentifier> SelectStreams(MembershipView view, StreamEvent trigger)
    {
        // Use projection to find affected memberships
        var evt = (MembershipDefinitionChangedEvent)trigger.Event;
        foreach (var membershipId in view.GetMemberships(evt.DefinitionId))
        {
            yield return new StreamIdentifier("Membership", membershipId);
        }
    }

    protected override RecalculateCommand BuildCommand(StreamIdentifier stream, MembershipView view, StreamEvent trigger)
    {
        return new RecalculateCommand(stream.Identifier, "Definition changed");
    }
}

// Execute
await ReactionRunner.CatchUpAsync(
    eventStore,
    projectionStore,
    reaction,
    folder,
    decider);
```

### 🔐 Metadata-Based Expected Version Support

**CQRS Stale-Read Protection**:
- Commands can now require expected version via metadata instead of command payload
- Expected version is request context, not command data
- `CommandAttribute` now supports `ExpectedVersionKey` property
- Replaces deprecated `CommandVersionMode` and `IExpectedVersionCommand`

**Benefits**:
- Commands remain simple and focused on business intent
- Expected version is consumed by StateRunner, not persisted with events
- Same command type can be used with or without expected version
- Clear separation between command data and execution context

**Example**:
```csharp
[Command("Order", ExpectedVersionKey = "ExpectedVersion")]
public sealed record ApproveOrder(string OrderId);

// User observed version 5 from read model
var order = await readModel.GetOrder("order-1"); // returns version 5

// Command will only execute if stream is still at version 5
await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    new ApproveOrder("order-1"),
    metadata: [
        new AppendMetadata("ExpectedVersion", order.Version),
        new AppendMetadata("CorrelationId", correlationId)
    ]);
```

**Breaking Changes** (minor impact):
- ⚠️ `CommandVersionMode` enum removed
- ⚠️ `IExpectedVersionCommand` interface removed
- ✅ Metadata-based approach is cleaner and more flexible
- ✅ `ExecuteAtVersionAsync` remains for explicit version control

See [Rickten.Aggregator README](./Rickten.Aggregator/README.md) for details.

### 🔧 Enhanced Projection Storage

**Namespace Support**:
- `IProjectionStore` now supports namespaces (default: `"system"`)
- Public projections use `"system"` namespace
- Reactions use `"reaction"` namespace for private projections
- Enables sharing the same database/repository for all projections

**Benefits**:
- Simplified infrastructure (one database, one projection table)
- Logical separation via namespaces
- Same `IProjectionStore` implementation for all scenarios
- Backward compatible with existing code

### ⚡ Performance Improvements

**Dual-Stream Event Processing**:
- Reactions now merge two filtered event streams by global position
- Projection stream: All events needed by the projection
- Trigger stream: Only events that trigger commands
- Optimal database queries with appropriate filters
- Clean merge-sort implementation via `MergeEventStreamsByPosition`

### 📦 Additional Enhancements

**ProjectionRunner**:
- New `RebuildUntilAsync` method for bounded projection rebuilds
- Useful for reactions needing historical projection state
- Pure rebuild method (no persistence)

**ProjectionRunner.CatchUpAsync**:
- Added optional `namespace` parameter (default: `"system"`)
- Maintains backward compatibility

## Implementer Note

### IProjectionStore Interface Enhancement

**Custom implementers must update** - The `IProjectionStore` interface now requires namespace-aware overloads:

```csharp
public interface IProjectionStore
{
    // Existing overload (unchanged)
    Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        CancellationToken cancellationToken = default);

    // New overload - custom implementers must add
    Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        string @namespace,
        CancellationToken cancellationToken = default);

    // Same pattern for SaveProjectionAsync
    Task SaveProjectionAsync<TState>(...);
    Task SaveProjectionAsync<TState>(..., string @namespace, ...);
}
```

**Who needs to update**:
- ✅ **Standard users**: No changes needed (official `ProjectionStore` already updated)
- ⚠️ **Custom implementers**: Must implement new overloads

**Implementation pattern**:
```csharp
public class MyCustomProjectionStore : IProjectionStore
{
    // First overload delegates to second with "system" default
    public Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        CancellationToken cancellationToken = default)
    {
        return LoadProjectionAsync<TState>(projectionKey, "system", cancellationToken);
    }

    // Second overload contains actual implementation
    public Task<Projection<TState>?> LoadProjectionAsync<TState>(
        string projectionKey,
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        // Filter by namespace: WHERE ProjectionKey = @key AND Namespace = @namespace
    }

    // Same pattern for SaveProjectionAsync...
}
```

See [UPGRADE_v1.1.md](./UPGRADE_v1.1.md) for detailed implementation guidance.

## Database Migration

### Migration: `AddProjectionNamespace`

**Changes**:
1. Adds `Namespace` column to `Projections` table (default: `"system"`)
2. Changes primary key from `ProjectionKey` to composite `(Namespace, ProjectionKey)`
3. Sets all existing projections to `"system"` namespace

**Apply Migration**:
```bash
# Using dotnet CLI
dotnet ef database update --project Rickten.EventStore.EntityFramework

# Using Package Manager Console
Update-Database -Project Rickten.EventStore.EntityFramework
```

**Backward Compatible**: All existing projections are automatically migrated to `"system"` namespace.

## Installation

### NuGet Packages

```bash
dotnet add package Rickten.EventStore --version 1.1.0
dotnet add package Rickten.EventStore.EntityFramework --version 1.1.0
dotnet add package Rickten.Aggregator --version 1.1.0
dotnet add package Rickten.Projector --version 1.1.0
dotnet add package Rickten.Reactor --version 1.1.0  # NEW
```

### Upgrade from v1.0

See [UPGRADE_v1.1.md](./UPGRADE_v1.1.md) for detailed upgrade instructions.

## Documentation

- **Getting Started**: See package README files
- **Upgrade Guide**: [UPGRADE_v1.1.md](./UPGRADE_v1.1.md)
- **Compatibility**: [COMPATIBILITY_v1.1.md](./COMPATIBILITY_v1.1.md)
- **Design Concepts**: [Rickten.Reactor/DESIGN_TRIANGLE.md](./Rickten.Reactor/DESIGN_TRIANGLE.md)
- **Migration Guide**: [Migrations/README.md](./Rickten.EventStore.EntityFramework/Migrations/README.md)

## Requirements

- **.NET**: 10.0 or later
- **Database Providers**: SQL Server, PostgreSQL, SQLite, MySQL
- **Entity Framework Core**: 9.0 or later

## Known Issues

None at this time.

## Contributors

Special thanks to all contributors who made this release possible!

## Feedback

We welcome your feedback:
- **Issues**: https://github.com/zebrai2/rickten.eventstore/issues
- **Discussions**: https://github.com/zebrai2/rickten.eventstore/discussions

## What's Next (v1.2 Roadmap)

- [ ] Hosted service support for continuous reaction processing
- [ ] Reaction scheduling and retry policies
- [ ] Performance benchmarks and optimization
- [ ] Additional projection store implementations (Redis, Cosmos DB)
- [ ] Distributed tracing support (OpenTelemetry)

## License

MIT License - See [LICENSE](./LICENSE) for details.

---

**Full Changelog**: https://github.com/zebrai2/rickten.eventstore/compare/v1.0.0...v1.1.0

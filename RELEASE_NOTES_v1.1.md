# Rickten v1.1.0 Release Notes

**Release Date**: TBD  
**Repository**: https://github.com/zebrai2/rickten.eventstore

## Overview

Rickten 1.1 introduces **projection namespaces** and **metadata-based expected version support**.

Most applications using the official Entity Framework projection store can upgrade without changing call sites. Custom `IProjectionStore` implementations must be updated to support the new namespace-aware projection storage contract.

### Compatibility Summary

- **Application-level compatible** for standard usage
- **Custom store implementers must update** (interface change)

This is a feature release with a clear implementer note.

## What's New

### 🔐 Metadata-Based Expected Version Support

**CQRS Stale-Read Protection**:
- Commands can now require expected version via metadata instead of command payload
- Expected version is request context, not command data
- `CommandAttribute` now supports `ExpectedVersionKey` property
- Replaces deprecated `CommandVersionMode` and `IExpectedVersionCommand`

**Benefits**:
- Commands remain simple and focused on business intent
- Expected version is consumed by StateRunner, not persisted with events
- Expected version is supplied through metadata, not the command payload
- Clear separation between command data and execution context

**Example**:
```csharp
[Command("Order", ExpectedVersionKey = "ExpectedVersion")]
public sealed record ApproveOrder(string OrderId);

var registry = scope.ServiceProvider.GetRequiredService<ITypeMetadataRegistry>();

// User observed version 5 from read model
var order = await readModel.GetOrder("order-1"); // returns version 5

// Command will only execute if stream is still at version 5
await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    new ApproveOrder("order-1"),
    registry,
    metadata: [
        new AppendMetadata("ExpectedVersion", order.Version),
        new AppendMetadata("CorrelationId", correlationId)
    ]);
```

**Breaking Changes** (minor impact):
- ⚠️ `CommandVersionMode` enum removed
- ⚠️ `IExpectedVersionCommand` interface removed
- ✅ Metadata-based approach is cleaner and more flexible

See [Rickten.Aggregator README](./Rickten.Aggregator/README.md) for details.

### 🔧 Enhanced Projection Storage

**Namespace Support**:
- `IProjectionStore` now supports namespaces (default: `"system"`)
- Public projections use `"system"` namespace
- Enables logical separation of different projection types
- Allows sharing the same database/repository for all projections

**Benefits**:
- Simplified infrastructure (one database, one projection table)
- Logical separation via namespaces
- Same `IProjectionStore` implementation for all scenarios
- Backward compatible with existing code

### 📦 Additional Enhancements

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
```

### Upgrade from v1.0

See [UPGRADE_v1.1.md](./UPGRADE_v1.1.md) for detailed upgrade instructions.

## Documentation

- **Getting Started**: See package README files
- **Upgrade Guide**: [UPGRADE_v1.1.md](./UPGRADE_v1.1.md)
- **Compatibility**: [COMPATIBILITY_v1.1.md](./COMPATIBILITY_v1.1.md)
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

- [ ] Event-driven command execution (Reactor)
- [ ] Hosted service support for continuous processing
- [ ] Performance benchmarks and optimization
- [ ] Additional projection store implementations (Redis, Cosmos DB)
- [ ] Distributed tracing support (OpenTelemetry)

## License

MIT License - See [LICENSE](./LICENSE) for details.

---

**Full Changelog**: https://github.com/zebrai2/rickten.eventstore/compare/v1.0.0...v1.1.0

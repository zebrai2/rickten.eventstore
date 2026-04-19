# Architecture Update Summary

## Overview

This document summarizes the major architecture changes made to Rickten.Aggregator to improve safety, testability, and clarity of responsibilities.

## What Changed

### Core Architecture

**Before (v1.0):**
- Static `StateRunner` utility class with methods:
  - `LoadStateAsync` - Load state from events + snapshots
  - `ExecuteAsync` - Execute command workflow
- Many parameters passed to each method call
- Folded events **before** persisting them
- No clear separation between persistence and orchestration

**After (v1.1+):**
- `IAggregateRepository<TState>` - DDD Repository pattern for persistence
  - `LoadStateAsync` - Load state from events + snapshots
  - `AppendEventsAsync` - Persist events to event store  
  - `SaveSnapshotIfNeededAsync` - Apply events and save snapshot at interval
- `AggregateCommandExecutor<TState, TCommand>` - Command workflow orchestrator
  - `ExecuteAsync` - Orchestrate: load → decide → persist → fold
- Dependencies injected via constructor (DI-friendly)
- **Persists events FIRST, folds SECOND** (safer architecture)
- Clear separation: repository = persistence, executor = orchestration

### Key Principle: Events First, State Second

```
Old: Load → Decide → Fold → Persist  ❌ (state computed before events saved)
New: Load → Decide → Persist → Fold  ✅ (events saved before state computed)
```

**Why this matters:**
- Events are the source of truth and must be persisted first
- If folding/snapshotting fails, events are already safe
- State can be reconstructed by replaying events
- Snapshots are optimization, never source of truth

## Files Changed

### Production Code

| File | Change | Description |
|------|--------|-------------|
| `Rickten.Aggregator\IAggregateRepository.cs` | Created | Interface for aggregate persistence (DDD Repository pattern) |
| `Rickten.Aggregator\AggregateRepository.cs` | Created | Implementation of aggregate repository with load, append, snapshot methods |
| `Rickten.Aggregator\AggregateCommandExecutor.cs` | Created | Command execution orchestrator |
| `Rickten.Aggregator\IStateLoader.cs` | Deleted (renamed) | Old interface, replaced by IAggregateRepository |
| `Rickten.Aggregator\StateLoader.cs` | Deleted (renamed) | Old implementation, replaced by AggregateRepository |
| `Rickten.Aggregator\StateRunner.cs` | Deleted | Static utilities replaced by instance-based components |

### Test Code

| File | Change | Description |
|------|--------|-------------|
| `Rickten.Aggregator.Tests\AggregateRepositoryTests.cs` | Renamed + Enhanced | Was `StateRunnerInvariantTests.cs`, added 9 new tests |
| `Rickten.Aggregator.Tests\CommandVersionModeTests.cs` | Updated | Uses AggregateCommandExecutor instead of StateRunner |
| `Rickten.Aggregator.Tests\SnapshotTests.cs` | Updated | Uses AggregateCommandExecutor instead of StateRunner |
| `Rickten.Aggregator.Tests\*` | Updated | All tests updated to use new architecture |
| `Rickten.Reactor.Tests\ReactionRunnerTests.cs` | Updated | Uses AggregateCommandExecutor (reactions execute commands) |

### Documentation

| File | Status | Description |
|------|--------|-------------|
| `Rickten.Aggregator\README.md` | Updated | Updated all examples to use new architecture |
| `Rickten.Aggregator\ARCHITECTURE.md` | Created | Comprehensive architecture documentation |
| `Rickten.Aggregator\MIGRATION_GUIDE.md` | Created | Step-by-step migration guide from v1.0 to v1.1 |

## Test Coverage

### Tests Added (9 new tests in AggregateRepositoryTests.cs)

**AppendEventsAsync tests (4):**
1. `AppendEventsAsync_WithValidVersion_SuccessfullyAppendsEvents` - Success case
2. `AppendEventsAsync_WithVersionConflict_ThrowsStreamVersionConflictException` - Conflict handling
3. `AppendEventsAsync_WithMultipleEvents_AppendsAllAtomically` - Atomic batch append
4. `AppendEventsAsync_WithEmptyEventsList_ReturnsEmptyList` - Edge case

**SaveSnapshotIfNeededAsync tests (5):**
1. `SaveSnapshotIfNeededAsync_AppliesEventsToState` - Event folding
2. `SaveSnapshotIfNeededAsync_AtIntervalBoundary_SavesSnapshot` - Snapshot at interval
3. `SaveSnapshotIfNeededAsync_NotAtBoundary_OnlyAppliesEvents` - No snapshot when not at boundary
4. `SaveSnapshotIfNeededAsync_WithNoSnapshotStore_StillAppliesEvents` - Graceful degradation
5. `SaveSnapshotIfNeededAsync_WithEmptyEvents_ReturnsCurrentState` - Edge case

### Test Results

- **Total tests:** 95 (68 Aggregator + 27 Reactor)
- **Passing:** 95 (100%)
- **Failing:** 0
- **Build:** ✅ Success

## Breaking Changes

### 1. StateRunner Removed

**Before:**
```csharp
await StateRunner.ExecuteAsync(eventStore, folder, decider, streamId, command, registry, snapshotStore, metadata);
await StateRunner.LoadStateAsync(eventStore, folder, streamId, registry, snapshotStore);
```

**After:**
```csharp
// Configure DI
services.AddTransient<AggregateCommandExecutor<TState, TCommand>>();
services.AddTransient<IAggregateRepository<TState>, AggregateRepository<TState>>();

// Execute
var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<TState, TCommand>>();
await executor.ExecuteAsync(streamId, command, metadata);

// Load
var repository = serviceProvider.GetRequiredService<IAggregateRepository<TState>>();
await repository.LoadStateAsync(streamId);
```

### 2. IStateLoader → IAggregateRepository

**Before:**
```csharp
public interface IStateLoader<TState>
{
    Task<(TState State, long Version)> LoadStateAsync(...);
    Task AppendEventsAsync(...);
    Task ApplyEvents(...);
}
```

**After:**
```csharp
public interface IAggregateRepository<TState>
{
    Task<(TState State, long Version)> LoadStateAsync(...);
    Task<IReadOnlyList<StreamEvent>> AppendEventsAsync(...);
    Task<TState> SaveSnapshotIfNeededAsync(...);
}
```

**Key differences:**
- Renamed to follow DDD Repository pattern
- `ApplyEvents` removed from interface (internal to SaveSnapshotIfNeededAsync)
- `SaveSnapshotIfNeededAsync` takes `IReadOnlyList<StreamEvent>` instead of separate events + version
- Clearer method names and responsibilities

### 3. Execution Order Changed

**Before:** Load → Decide → Fold → Persist

**After:** Load → Decide → **Persist → Fold**

**Impact:** More robust architecture (events saved before derived state computed), but behavior is the same for users.

## Migration Path

### Simple Migration (3 steps)

1. **Update DI configuration:**
   ```csharp
   services.AddSingleton<IStateFolder<TState>, TStateFolder>();
   services.AddSingleton<ICommandDecider<TState, TCommand>, TCommandDecider>();
   services.AddTransient<IAggregateRepository<TState>, AggregateRepository<TState>>();
   services.AddTransient<AggregateCommandExecutor<TState, TCommand>>();
   ```

2. **Replace StateRunner.ExecuteAsync:**
   ```csharp
   var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<TState, TCommand>>();
   var (state, version, events) = await executor.ExecuteAsync(streamId, command, metadata);
   ```

3. **Replace StateRunner.LoadStateAsync (if used):**
   ```csharp
   var repository = serviceProvider.GetRequiredService<IAggregateRepository<TState>>();
   var (state, version) = await repository.LoadStateAsync(streamId);
   ```

See `MIGRATION_GUIDE.md` for detailed migration instructions.

## Benefits

### 1. Safer Architecture

- ✅ Events persisted **before** state derivation
- ✅ If folding fails, events are already safe
- ✅ State can be reconstructed from events
- ✅ Snapshots are optimization, never source of truth

### 2. Better Testability

- ✅ Instance-based components (easy to mock)
- ✅ Clear dependencies (constructor injection)
- ✅ Can test repository and executor independently
- ✅ Domain logic (folder, decider) remains pure

### 3. Clearer Responsibilities

- ✅ **StateFolder:** Pure state transitions (events → state)
- ✅ **CommandDecider:** Pure business logic (state + command → events)
- ✅ **AggregateRepository:** Persistence lifecycle (load, append, snapshot)
- ✅ **AggregateCommandExecutor:** Command workflow orchestration

### 4. DDD Alignment

- ✅ `IAggregateRepository` follows DDD Repository pattern
- ✅ Clean separation between domain and infrastructure
- ✅ Aggregate root encapsulation
- ✅ Industry-standard naming and patterns

### 5. Better DI Support

- ✅ Instance-based instead of static methods
- ✅ Dependencies injected via constructor
- ✅ Follows ASP.NET Core best practices
- ✅ Lifetime management (Singleton vs Transient)

## Backward Compatibility

### Breaking Changes

- ❌ `StateRunner` class removed
- ❌ `IStateLoader` renamed to `IAggregateRepository`
- ❌ Method signatures changed (fewer parameters, DI-based)

### Non-Breaking

- ✅ `IStateFolder<TState>` - unchanged
- ✅ `ICommandDecider<TState, TCommand>` - unchanged
- ✅ `StateFolder<TState>` base class - unchanged
- ✅ `CommandDecider<TState, TCommand>` base class - unchanged
- ✅ `[Aggregate]`, `[Command]`, `[Event]` attributes - unchanged
- ✅ Domain model (states, commands, events) - unchanged

**Migration effort:** Low to moderate (mostly DI configuration changes)

## Performance Impact

### No Performance Degradation

- ✅ Same number of I/O operations (read events, write events, optional snapshot)
- ✅ Persist-then-fold is safer, not slower
- ✅ DI overhead is negligible (constructor injection happens once)
- ✅ Snapshots work identically (automatic at configured intervals)

### Potential Improvements

- ✅ Repository can be optimized independently (caching, connection pooling)
- ✅ Executor can add cross-cutting concerns (logging, metrics, tracing)
- ✅ Better for long-lived services (DI container manages lifetimes)

## Documentation Updates

### New Documentation

1. **`ARCHITECTURE.md`** - Comprehensive architecture guide
   - Core principles (Events First, Separation of Concerns)
   - Component responsibilities
   - Data flow diagrams
   - Snapshot strategy
   - Error handling
   - Testing strategy

2. **`MIGRATION_GUIDE.md`** - Step-by-step migration from v1.0 to v1.1
   - What changed and why
   - Step-by-step migration instructions
   - Before/after code examples
   - Common migration patterns
   - Testing migration
   - FAQ

### Updated Documentation

1. **`README.md`** - Updated all examples
   - Replaced StateRunner with AggregateCommandExecutor
   - Updated DI configuration examples
   - Updated usage examples
   - Updated design principles section

## Next Steps for Users

1. **Read `MIGRATION_GUIDE.md`** for migration instructions
2. **Read `ARCHITECTURE.md`** to understand the new architecture
3. **Update DI configuration** to register new components
4. **Replace StateRunner calls** with executor/repository calls
5. **Run tests** to verify everything works
6. **Review and update** any custom infrastructure code

## Support

- GitHub Issues: Report migration problems or questions
- Documentation: Comprehensive guides in `ARCHITECTURE.md` and `MIGRATION_GUIDE.md`
- Test Suite: `Rickten.Aggregator.Tests` has working examples of all patterns

## Timeline

- **Development:** Feature branch `feature/aggregate-command-executor`
- **Testing:** All 95 tests passing (68 Aggregator + 27 Reactor)
- **Documentation:** Complete (README, ARCHITECTURE, MIGRATION_GUIDE)
- **Status:** Ready for review/merge

## Summary

This architecture update represents a significant improvement in safety, testability, and clarity:

- **Events are now persisted before state derivation** (safer by design)
- **DDD Repository pattern** provides clean separation of concerns
- **Instance-based components** are more testable and DI-friendly
- **Clear responsibilities** make the codebase easier to understand and maintain
- **Comprehensive documentation** guides users through migration

The migration is straightforward (mostly DI configuration changes), and all existing domain logic (StateFolder, CommandDecider) remains unchanged.

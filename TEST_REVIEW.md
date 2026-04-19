# Rickten.Aggregator Test Suite Review

## Executive Summary

The test suite is **generally good** with comprehensive coverage of edge cases and guardrails. Recent architectural refactoring has clarified responsibilities.

**Recent Architecture Changes:**
- ✅ **StateLoader renamed to AggregateRepository** - Following DDD Repository pattern
- ✅ **Refined IAggregateRepository interface** - Now has 3 clear methods:
  - `LoadStateAsync()` - Read aggregate state from events (+ snapshots)
  - `AppendEventsAsync()` - Persist events to event store (pure I/O)
  - `SaveSnapshotIfNeededAsync()` - Apply events to state + save snapshot if needed
- ✅ **Events persist first** - Safer architecture (append before fold)
- ⚠️ **Test file names not updated** to reflect new naming

---

## Current IAggregateRepository API

```csharp
public interface IAggregateRepository<TState>
{
    // Read: Load state from events + snapshots
    Task<(TState State, long Version)> LoadStateAsync(
        StreamIdentifier streamIdentifier,
        CancellationToken cancellationToken = default);

    // Write: Persist events (optimistic concurrency)
    Task<IReadOnlyList<StreamEvent>> AppendEventsAsync(
        StreamPointer expectedVersion,
        IReadOnlyList<AppendEvent> events,
        CancellationToken cancellationToken = default);

    // Fold + Snapshot: Apply events and maybe save snapshot
    Task<TState> SaveSnapshotIfNeededAsync(
        TState currentState,
        IReadOnlyList<object> events,
        StreamPointer newVersion,
        CancellationToken cancellationToken = default);
}
```

---

## Test File Analysis

### 1. **StateRunnerInvariantTests.cs** ⚠️ NEEDS RENAMING & REFACTORING

**What it tests:**
- AggregateRepository.LoadStateAsync behavior (happy path, empty streams, snapshots)
- Stream validation invariants (gaps, duplicates, null events)
- One test for AggregateCommandExecutor concurrency

**Current Test Coverage:**
- ✅ LoadStateAsync with valid stream
- ✅ LoadStateAsync with empty stream
- ✅ LoadStateAsync with snapshots
- ✅ LoadStateAsync version continuity validation
- ✅ Documentation tests for invariants (stream mismatch, gaps, duplicates, null events)
- ✅ One ExecuteAsync concurrency conflict test

**Issues:**
- ❌ **OBSOLETE NAME**: Still called "StateRunnerInvariantTests" but StateRunner was renamed to AggregateRepository
- ❌ **MIXED CONCERNS**: Tests both AggregateRepository (most tests) AND AggregateCommandExecutor (1 test)
- ❌ **MISSING TESTS**: No direct tests for AggregateRepository.AppendEventsAsync()
- ❌ **MISSING TESTS**: No tests for AggregateRepository.SaveSnapshotIfNeededAsync() with events parameter
- ⚠️ **OLD SIGNATURE**: SaveSnapshotIfNeededAsync tests use old signature (streamPointer, state) not new (state, events, streamPointer)
- ✅ Good coverage of LoadStateAsync edge cases
- ✅ Good documentation of invariants

**Recommendations:**
1. **Rename file** to `AggregateRepositoryTests.cs`
2. **Update class comment** to reflect testing AggregateRepository, not StateRunner
3. **Move concurrency test** to AggregateCommandExecutorTests.cs (new file)
4. **Add AppendEventsAsync tests:**
   - AppendEventsAsync_WithValidVersion_SuccessfullyAppends
   - AppendEventsAsync_WithVersionConflict_ThrowsStreamVersionConflictException
   - AppendEventsAsync_WithMultipleEvents_AppendsAll
5. **Add SaveSnapshotIfNeededAsync tests with new signature:**
   - SaveSnapshotIfNeededAsync_AppliesEventsToState
   - SaveSnapshotIfNeededAsync_AtInterval_SavesSnapshot
   - SaveSnapshotIfNeededAsync_NotAtInterval_DoesNotSaveSnapshot
   - SaveSnapshotIfNeededAsync_WithNoSnapshotStore_StillAppliesEvents

---

### 2. **CommandVersionModeTests.cs** ✅ EXCELLENT

**What it tests:**
- Command [Command] attribute for expected version configuration
- Expected version validation in AggregateCommandExecutor
- Metadata parsing and type conversion (int, string, short → long)
- Optimistic concurrency exception handling
- Idempotent commands with expected version
- Metadata filtering (expected version key not persisted in events)

**Current Test Coverage:**
- ✅ CommandAttribute default and custom ExpectedVersionKey
- ✅ ExecuteAsync without expected version (uses latest state)
- ✅ ExecuteAsync with matching expected version
- ✅ ExecuteAsync with version mismatch (throws StreamVersionConflictException)
- ✅ ExecuteAsync with missing/invalid/null metadata
- ✅ ExecuteAsync metadata type conversions (int, string, short)
- ✅ ExecuteAsync doesn't run decider on version mismatch
- ✅ ExecuteAsync with snapshots still validates expected version
- ✅ Idempotent commands with expected version
- ✅ Expected version key filtered from event metadata
- ✅ Command not in registry throws

**Issues:**
- ✅ Well organized and comprehensive
- ✅ Tests the full ExecuteAsync workflow through AggregateCommandExecutor
- ✅ Uses integration approach (real EventStore → AggregateRepository → AggregateCommandExecutor)
- ℹ️ All 16 tests passing

**Recommendations:**
- ✅ No changes needed - this file is exemplary
- Consider adding one test: `ExecuteAsync_WithExpectedVersion_EventsPersistBeforeStateFolded` to document order of operations

---

### 3. **SnapshotTests.cs** ✅ GOOD, MINOR UPDATES NEEDED

**What it tests:**
- Snapshot configuration via [Aggregate] attribute
- Snapshot interval logic
- Snapshot persistence and loading
- Idempotent commands don't trigger snapshots
- LoadStateAsync with/without snapshots

**Current Test Coverage:**
- ✅ AggregateAttribute default snapshot interval (0)
- ✅ AggregateAttribute can set snapshot interval
- ✅ StateFolder exposes SnapshotInterval property
- ✅ ExecuteAsync without snapshot store doesn't snapshot
- ✅ ExecuteAsync with snapshot store but no interval doesn't snapshot
- ✅ ExecuteAsync with snapshot interval saves snapshots at interval
- ✅ ExecuteAsync snapshots only at exact interval (not every event)
- ✅ ExecuteAsync idempotent command doesn't snapshot
- ✅ LoadStateAsync with snapshot starts from snapshot
- ✅ LoadStateAsync without snapshot loads from beginning

**Issues:**
- ✅ Well organized
- ✅ Tests both AggregateRepository (LoadStateAsync with snapshots) and AggregateCommandExecutor (ExecuteAsync snapshot persistence)
- ⚠️ Tests use full integration stack - could benefit from some isolated unit tests
- ⚠️ **OUTDATED**: Tests may be calling SaveSnapshotIfNeededAsync with old signature (needs verification)

**Recommendations:**
- ✅ Keep existing integration tests - they're valuable
- Consider adding isolated unit tests for AggregateRepository.SaveSnapshotIfNeededAsync:
  - `SaveSnapshotIfNeededAsync_WithEvents_AppliesEventsToState`
  - `SaveSnapshotIfNeededAsync_AtIntervalBoundary_SavesSnapshot`
  - `SaveSnapshotIfNeededAsync_NotAtBoundary_OnlyAppliesEvents`
  - `SaveSnapshotIfNeededAsync_WithNoSnapshotStore_StillFoldsEvents`

---

### 4. **CommandDeciderGuardrailTests.cs** ✅ EXCELLENT

**What it tests:**
- CommandDecider guardrails (null checks, aggregate validation)
- Helper methods (Require, NoEvents, MultipleEvents)
- Aggregate name and stream identifier creation

**Issues:**
- ✅ Pure unit tests - fast and focused
- ✅ Comprehensive coverage of validation logic
- ✅ Good use of test naming conventions

**Recommendation:**
- No changes needed - exemplary test file

---

### 5. **TraceIdentityAggregatorTests.cs** ✅ GOOD

**What it tests:**
- CorrelationId and CausationId preservation
- BatchId behavior (shared within command, distinct across commands)
- EventId uniqueness

**Issues:**
- ✅ Tests important distributed tracing concerns
- ✅ Uses full integration stack appropriately
- ℹ️ Focused scope - tests one specific concern

**Recommendation:**
- No changes needed

---

### 6. **ValidationTests.cs** ✅ GOOD

**What it tests:**
- StateFolder construction validation
- Event handler coverage validation
- Event handler routing
- Unknown event handling

**Issues:**
- ✅ Pure unit tests
- ✅ Tests critical compile-time and runtime validation
- ✅ Good coverage of StateFolder behavior

**Recommendation:**
- No changes needed

---

## Gap Analysis

### Critical Gaps 🔴

1. **No unit tests for AggregateRepository.AppendEventsAsync()**
   - This method persists events to the EventStore
   - Should have tests for:
     - Successful append
     - Version conflict (StreamVersionConflictException)
     - Multiple events appended atomically
     - Currently only tested indirectly through AggregateCommandExecutor

2. **No tests for new SaveSnapshotIfNeededAsync signature**
   - Method signature changed to take `(state, events, newVersion)` instead of `(streamPointer, state)`
   - New responsibility: **apply events first, then snapshot**
   - Should test:
     - Events are applied to state correctly
     - Snapshot saved at interval boundaries
     - Snapshot not saved when not at boundary
     - Works without snapshot store (still folds events)

3. **No dedicated AggregateCommandExecutorTests.cs file**
   - Tests scattered across CommandVersionModeTests, SnapshotTests, TraceIdentityAggregatorTests
   - Should have tests for core workflow orchestration:
     - Order of operations (load → decide → append → fold → snapshot)
     - Idempotent command short-circuit
     - Metadata filtering
     - Error handling

### Important Gaps 🟡

4. **Missing negative tests for AppendEventsAsync**
   - What happens on network failure during append?
   - What happens if events is empty list?
   - Null event in list?

5. **Limited testing of SaveSnapshotIfNeededAsync edge cases**
   - What if events list is empty?
   - What if state is null?
   - What if folding throws an exception?

6. **No performance/stress tests**
   - Large event streams (LoadStateAsync with 10,000+ events)
   - Many snapshots
   - High concurrency scenarios

### Nice to Have 🟢

7. **No tests for CancellationToken behavior**
   - All async methods accept CancellationToken but not tested
   - Should test cancellation propagates correctly

8. **Limited mocking/unit tests**
   - Most tests use full integration stack (EventStore)
   - Could benefit from unit tests with mocked IEventStore, ISnapshotStore
   - Faster test execution
   - Easier to test edge cases

---

## Organizational Issues

### File Naming

- ❌ `StateRunnerInvariantTests.cs` - obsolete name, should be `AggregateRepositoryTests.cs`

### Test Organization

**Current structure (by feature):**
```
CommandDeciderGuardrailTests.cs       ← Tests CommandDecider
CommandVersionModeTests.cs            ← Tests AggregateCommandExecutor (expected version feature)
SnapshotTests.cs                      ← Tests AggregateRepository + AggregateCommandExecutor (snapshot feature)
StateRunnerInvariantTests.cs          ← Tests AggregateRepository + one AggregateCommandExecutor test
TraceIdentityAggregatorTests.cs       ← Tests AggregateCommandExecutor (trace identity feature)
ValidationTests.cs                    ← Tests StateFolder
```

**Problems:**
- AggregateRepository tests split between StateRunnerInvariantTests and SnapshotTests
- AggregateCommandExecutor tests split across multiple feature files
- No dedicated test file for core AggregateRepository responsibilities
- Missing tests for new AppendEventsAsync and updated SaveSnapshotIfNeededAsync

**Recommended structure (by class under test):**
```
AggregateRepositoryTests.cs           ← ALL AggregateRepository tests
  - LoadStateAsync (with variants, edge cases, invariants)
  - AppendEventsAsync (success, conflicts, edge cases)
  - SaveSnapshotIfNeededAsync (fold + snapshot logic)

StateFolder Tests.cs                   ← ALL StateFolder tests (rename ValidationTests.cs)
  - Event handler validation
  - Folding logic
  - Unknown event handling

CommandDeciderTests.cs                ← Rename from CommandDeciderGuardrailTests.cs
  - Guardrails and helpers
  - Aggregate validation

AggregateCommandExecutorTests.cs      ← NEW: Core orchestration tests
  - Workflow orchestration
  - Idempotent commands
  - Error handling

Feature-specific test files (optional, keep if focused):
  - CommandVersionModeTests.cs        ← Expected version feature
  - SnapshotIntegrationTests.cs       ← End-to-end snapshot scenarios
  - TraceIdentityTests.cs             ← Distributed tracing
```

**Alternative (keep feature-based organization):**
If you prefer feature-based organization, at minimum:
1. Rename StateRunnerInvariantTests → AggregateRepositoryTests
2. Add missing AggregateRepository method tests to appropriate files
3. Ensure each class has at least one dedicated test file

---

## Test Quality Observations

### ✅ Strengths

1. **Excellent edge case coverage** - null checks, validation, error conditions
2. **Good use of documentation tests** - documenting invariants that can't be tested
3. **Clear naming conventions** - test names follow Given_When_Then pattern
4. **Integration tests** - good coverage of real workflow
5. **Domain models for testing** - clean test fixtures

### ⚠️ Weaknesses

1. **Over-reliance on integration tests** - many tests use full EventStore stack
2. **Mixed abstraction levels** - some files test both unit and integration concerns
3. **Limited use of mocking** - few tests isolate dependencies
4. **No test for new StateLoader responsibilities** - ApplyEvents, AppendEventsAsync

---

## Recommended Actions (Priority Order)

### High Priority 🔴

1. **Rename StateRunnerInvariantTests.cs → AggregateRepositoryTests.cs**
   - Update class name: `StateRunnerInvariantTests` → `AggregateRepositoryTests`
   - Update XML comments to reference AggregateRepository
   - Move ExecuteAsync_WithConcurrencyConflict to future AggregateCommandExecutorTests

2. **Add AggregateRepository.AppendEventsAsync() tests in AggregateRepositoryTests.cs**
   ```csharp
   [Fact]
   public async Task AppendEventsAsync_WithValidVersion_SuccessfullyAppendsEvents()

   [Fact]
   public async Task AppendEventsAsync_WithVersionConflict_ThrowsStreamVersionConflictException()

   [Fact]
   public async Task AppendEventsAsync_WithMultipleEvents_AppendsAllAtomically()

   [Fact]
   public async Task AppendEventsAsync_WithEmptyEventsList_ReturnsEmptyList()
   ```

3. **Add AggregateRepository.SaveSnapshotIfNeededAsync() tests with new signature**
   ```csharp
   [Fact]
   public async Task SaveSnapshotIfNeededAsync_AppliesEventsToState()

   [Fact]
   public async Task SaveSnapshotIfNeededAsync_AtIntervalBoundary_SavesSnapshot()

   [Fact]
   public async Task SaveSnapshotIfNeededAsync_NotAtBoundary_OnlyAppliesEvents()

   [Fact]
   public async Task SaveSnapshotIfNeededAsync_WithNoSnapshotStore_StillAppliesEvents()

   [Fact]
   public async Task SaveSnapshotIfNeededAsync_WithEmptyEvents_ReturnsCurrentState()
   ```

### Medium Priority 🟡

4. **Create AggregateCommandExecutorTests.cs for core workflow tests**
   - Move concurrency test from StateRunnerInvariantTests
   - Add workflow orchestration tests:
     ```csharp
     [Fact]
     public async Task ExecuteAsync_WorkflowOrder_LoadThenAppendThenFold()

     [Fact]
     public async Task ExecuteAsync_IdempotentCommand_ShortCircuitsBeforeAppend()

     [Fact]
     public async Task ExecuteAsync_FilteredMetadata_ExcludesExpectedVersionKey()
     ```

5. **Add negative/edge case tests for AppendEventsAsync**
   - Network failures (mock IEventStore to throw)
   - Invalid pointer
   - Null events in list

6. **Improve test organization documentation**
   - Add README.md in Rickten.Aggregator.Tests explaining file organization
   - Add XML comments to test classes explaining their scope and what they test

### Low Priority 🟢

7. **Add CancellationToken tests**
   - Test that cancellation propagates correctly through async methods

8. **Add performance/stress tests** (separate test category or project)
   - LoadStateAsync with large event streams
   - Snapshot effectiveness measurements

9. **Consider adding unit tests with mocks**
   - Use categories/traits to separate unit from integration tests
   - Faster test execution for local development

10. **Rename other files for clarity**
    - `CommandDeciderGuardrailTests.cs` → `CommandDeciderTests.cs`
    - `ValidationTests.cs` → `StateFolderTests.cs`

---

## Conclusion

The test suite is **solid with targeted improvements needed** for the new architecture. The biggest issues are:

1. **Naming** - StateRunnerInvariantTests needs renaming to AggregateRepositoryTests
2. **Coverage Gaps** - New/updated AggregateRepository methods need dedicated tests:
   - AppendEventsAsync (new responsibility)
   - SaveSnapshotIfNeededAsync (new signature with events parameter)
3. **Organization** - Tests scattered by feature rather than by class under test
4. **Architecture Alignment** - Tests need to reflect new responsibility allocation:
   - Events persist **first** (AppendEventsAsync)
   - State computed **second** (SaveSnapshotIfNeededAsync)

**Strengths:**
- ✅ Excellent edge case coverage for existing functionality
- ✅ Good integration test coverage of full workflows
- ✅ Clear test naming conventions
- ✅ CommandVersionModeTests is exemplary
- ✅ Good use of documentation tests for architectural invariants

**Priority Actions:**
1. Rename StateRunnerInvariantTests → AggregateRepositoryTests (5 min)
2. Add AppendEventsAsync tests (30 min)
3. Add SaveSnapshotIfNeededAsync tests with new signature (30 min)

**After these changes, the test suite will:**
- Properly reflect the new AggregateRepository architecture
- Have complete coverage of the 3-method repository interface
- Validate the new "persist first, fold second" pattern
- Be well-organized and maintainable

The architectural refactoring was successful - the code is cleaner and more aligned with DDD patterns. The test suite just needs to catch up with the new design.

# Changelog

All notable changes to Rickten.Aggregator will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-01-XX

### Added
- `IStateFolder<TState>` interface for event folding
- `ICommandDecider<TState, TCommand>` interface for command decision-making
- `StateFolder<TState>` abstract base class with:
  - `[Aggregate]` attribute requirement
  - Event coverage validation (strict by default) using explicit `When()` handler methods
  - Convention: `protected TState When(EventType e, TState state)` for each event
  - `InitialState()` override point
  - `EnsureValid()` helper method
  - `IgnoredEvents` property to exclude events from validation
  - `SnapshotInterval` property exposing configured snapshot interval
- `CommandDecider<TState, TCommand>` abstract base class with:
  - `[Aggregate]` attribute requirement
  - Command and event aggregate validation
  - Helper methods: `Event()`, `NoEvents()`, `Events()`, `Require()`, `RequireEqual()`, `RequireNotNull()`
  - `CreateStreamId()` helper for stream identifier creation
  - `ValidateCommand()` and `ExecuteCommand()` override points
- `[Aggregate]` attribute for marking implementations with:
  - Aggregate name (required)
  - `ValidateEventCoverage` property (default: true, validates When() handlers exist)
  - `SnapshotInterval` property (default: 0) for automatic snapshots
- `[Command]` attribute for marking commands with aggregate membership
- `StateRunner` static utilities:
  - `LoadStateAsync()` with:
    - Comprehensive stream validation (gaps, ordering, duplicates)
    - Optional snapshot optimization (loads from latest snapshot when provided)
  - `ExecuteAsync()` for command execution with:
    - Event folding
    - Optional automatic snapshot support based on `SnapshotInterval`
    - Snapshots saved at exact intervals (e.g., 50, 100, 150)
    - No snapshots for idempotent commands
- Comprehensive unit tests for snapshot functionality
- Complete README with examples and API documentation

### Design Principles
- Clean separation between state folding (read-side) and command decision-making (write-side)
- Strict validation by default: event coverage verified at construction via explicit handler methods
- Declarative snapshot configuration via `[Aggregate]` attribute
- Helper methods to reduce boilerplate
- Clear error messages for validation failures
- Type-safe aggregate boundaries enforced at runtime

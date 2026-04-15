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
  - Event coverage validation (strict by default)
  - `ApplyEvent()` override point
  - `EnsureValid()` helper method
  - `IgnoredEvents` property for explicit opt-out
- `CommandDecider<TState, TCommand>` abstract base class with:
  - `[Aggregate]` attribute requirement
  - Command and event aggregate validation
  - Helper methods: `Event()`, `NoEvents()`, `Events()`, `Require()`, `RequireEqual()`, `RequireNotNull()`
  - `CreateStreamId()` helper for stream identifier creation
  - `ValidateCommand()` and `ExecuteCommand()` override points
- `[Aggregate]` attribute for marking implementations with aggregate name
- `[Command]` attribute for marking commands with aggregate membership
- `StateRunner` static utilities:
  - `LoadStateAsync()` with comprehensive stream validation (gaps, ordering, duplicates)
  - `ExecuteAsync()` for command execution with event folding
- Comprehensive README with examples and API documentation

### Design Principles
- Clean separation between state folding (read-side) and command decision-making (write-side)
- Strict validation by default with opt-out capability
- Helper methods to reduce boilerplate
- Clear error messages for validation failures
- Type-safe aggregate boundaries enforced at runtime

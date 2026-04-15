# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- **Version semantics inconsistency**: Standardized `StreamPointer.Version` to consistently represent the current/last written stream version across all implementations. Previously, the documentation stated that `expectedVersion` should be the current version, but the EF implementation expected `currentVersion + 1`. Now all code and documentation agree: pass the current stream version (e.g., version 5 if stream has 5 events), and new events write at version + 1.

### Planned
- Azure Blob Storage snapshot provider
- Redis projection store provider
- Distributed event subscription support
- Event migration tooling

## [1.0.0] - 2025-01-XX

### Added
- Initial release of Rickten.EventStore
- Core event sourcing abstractions (`IEventStore`, `ISnapshotStore`, `IProjectionStore`)
- Entity Framework Core implementation with SQL Server, PostgreSQL, and SQLite support
- Optimistic concurrency control with version conflict detection
- Event metadata support for correlation and causation tracking
- Stream-based event storage and retrieval
- Global event position tracking for projections
- Snapshot support for aggregate state optimization
- Projection store with resumable position tracking
- Dependency injection extensions:
  - `AddEventStore()` - Register all stores together
  - `AddEventStoreOnly()` - Register event store separately
  - `AddSnapshotStoreOnly()` - Register snapshot store separately
  - `AddProjectionStoreOnly()` - Register projection store separately
  - `AddEventStoreInMemory()` - Quick in-memory setup for testing
  - `AddEventStoreSqlServer()` - Quick SQL Server setup
- Comprehensive XML documentation
- Full test coverage with 36+ unit tests
- Support for .NET 10 and C# 14

### Features
- **Event Sourcing**: Store and retrieve domain events with full history
- **Async/Await**: Modern async patterns with `IAsyncEnumerable`
- **Strong Typing**: Type-safe event handling with records and `EventAttribute`
- **Flexible Configuration**: Support for multiple database providers
- **Separate Store Registration**: Use different databases for events, snapshots, and projections
- **CQRS Support**: Separate read and write models
- **Event Filtering**: Filter by stream type and event type
- **Metadata Support**: Attach custom metadata to events

### Documentation
- Complete README with usage examples
- Dependency injection configuration guide
- Best practices and patterns
- Testing strategies
- MIT License

[Unreleased]: https://github.com/rickten/rickten.eventstore/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/rickten/rickten.eventstore/releases/tag/v1.0.0

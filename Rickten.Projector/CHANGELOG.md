# Changelog

All notable changes to Rickten.Projector will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-01-XX

### Added
- `IProjection<TView>` interface for building read model projections
- `Projection<TView>` abstract base class with:
  - Automatic filter extraction from `[Projection]` attribute
  - `ProjectionName`, `AggregateTypeFilter`, `EventTypeFilter` properties
  - Runtime validation that received events match configured filters
  - `ApplyEvent()` override point for projection logic
- `[Projection]` attribute for declarative filtering:
  - `Name` property for projection identification
  - `AggregateTypes` property for aggregate filtering
  - `EventTypes` property for event type filtering
  - `Description` property for documentation
- `ProjectionRunner` static utilities:
  - `RebuildAsync()` for manual projection rebuilding from specific version
  - `CatchUpAsync()` for automatic checkpoint management
  - Leverages `IEventStore.LoadAllAsync` with attribute-based filters
- Full `StreamEvent` access in projections (including metadata)
- Filter validation throws `InvalidOperationException` on mismatch
- Comprehensive README with examples and usage patterns
- MIT License and CHANGELOG

### Design Principles
- Projection-controlled checkpointing (each projection decides metadata usage)
- Efficient store-level filtering via `LoadAllAsync`
- Simple projection mechanics (manual rebuild or catch-up)
- Flexible checkpoint management
- Runtime filter validation for safety

Breaking Changes: None (initial release)

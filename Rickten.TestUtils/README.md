# Rickten.TestUtils

Shared test utilities for Rickten event sourcing test projects.

## Purpose

This project contains **reusable test helpers and utilities** that are shared across multiple test projects in the Rickten solution:

- `Rickten.EventStore.Tests`
- `Rickten.Aggregator.Tests`
- `Rickten.Reactor.Tests`
- `Rickten.Projector.Tests`

## Why a Separate Test Utils Project?

Test projects should not reference each other to avoid coupling test suites. This project provides a clean way to share test utilities without creating inter-test-project dependencies.

## Contents

### NoOpSnapshotStore

A no-op implementation of `ISnapshotStore` for testing purposes.

```csharp
using Rickten.TestUtils;

// Use in tests where snapshots are not needed
var repository = new AggregateRepository<MyState>(
    eventStore,
    folder,
    NoOpSnapshotStore.Instance);
```

**Note**: In production code, snapshots are optional. Simply omit `ISnapshotStore` from DI registration and the repository works fine without it. This test utility is only for tests that need to pass an `ISnapshotStore` instance explicitly.

## Usage

Test projects reference this project and use `global using Rickten.TestUtils;` in their `GlobalUsings.cs` to make utilities available throughout the test suite.

```csharp
// GlobalUsings.cs in test projects
global using Rickten.TestUtils;
global using Xunit;
```

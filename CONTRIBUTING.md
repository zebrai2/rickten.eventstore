# Contributing to Rickten.EventStore

First off, thank you for considering contributing to Rickten.EventStore! 🎉

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How Can I Contribute?](#how-can-i-contribute)
- [Development Setup](#development-setup)
- [Pull Request Process](#pull-request-process)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Commit Message Guidelines](#commit-message-guidelines)

## Code of Conduct

This project and everyone participating in it is governed by a Code of Conduct. By participating, you are expected to uphold this code. Please report unacceptable behavior to [your-email@example.com].

### Our Standards

- Be respectful and inclusive
- Accept constructive criticism gracefully
- Focus on what is best for the community
- Show empathy towards other community members

## How Can I Contribute?

### Reporting Bugs

Before creating bug reports, please check the existing issues to avoid duplicates. When creating a bug report, include as many details as possible:

- **Use a clear and descriptive title**
- **Describe the exact steps to reproduce the problem**
- **Provide specific examples** (code snippets, error messages)
- **Describe the behavior you observed** and what you expected
- **Include your environment details** (.NET version, OS, database provider)

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When creating an enhancement suggestion:

- **Use a clear and descriptive title**
- **Provide a detailed description** of the proposed functionality
- **Explain why this enhancement would be useful**
- **Include code examples** if applicable

### Pull Requests

- Fill in the required template
- Follow the coding standards
- Include tests for new functionality
- Update documentation as needed
- Ensure all tests pass

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 or later (or VS Code with C# extension)
- Git

### Getting Started

1. **Fork the repository**

```bash
# Clone your fork
git clone https://github.com/YOUR-USERNAME/rickten.eventstore.git
cd rickten.eventstore
```

2. **Create a branch**

```bash
git checkout -b feature/your-feature-name
# or
git checkout -b fix/your-bug-fix
```

3. **Restore packages**

```bash
dotnet restore
```

4. **Build the solution**

```bash
dotnet build
```

5. **Run tests**

```bash
dotnet test
```

### Project Structure

```
Rickten.EventStore/
├── Rickten.EventStore/              # Core abstractions
│   ├── IEventStore.cs
│   ├── ISnapshotStore.cs
│   ├── IProjectionStore.cs
│   └── ...
├── Rickten.EventStore.EntityFramework/  # EF Core implementation
│   ├── EventStore.cs
│   ├── SnapshotStore.cs
│   ├── ProjectionStore.cs
│   ├── EventStoreDbContext.cs
│   └── ...
└── Rickten.EventStore.Tests/       # Tests
    ├── EventStoreTests.cs
    ├── SnapshotStoreTests.cs
    └── ...
```

## Pull Request Process

1. **Update documentation** for any changed functionality
2. **Add or update tests** to cover your changes
3. **Ensure all tests pass** (`dotnet test`)
4. **Update CHANGELOG.md** with a description of your changes
5. **Follow the commit message guidelines** (see below)
6. **Request review** from maintainers

### Pull Request Checklist

- [ ] Code builds without errors
- [ ] All tests pass
- [ ] New tests added for new functionality
- [ ] Documentation updated
- [ ] CHANGELOG.md updated
- [ ] No breaking changes (or clearly documented)
- [ ] Code follows project style guidelines

## Coding Standards

### General Guidelines

- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use **meaningful names** for variables, methods, and classes
- Keep methods **small and focused** (single responsibility)
- **Comment complex logic** but prefer self-documenting code
- Use **C# 14 features** where appropriate (records, pattern matching, etc.)

### Code Style

```csharp
// ✅ Good
public async Task<StreamEvent> AppendEventAsync(
    StreamPointer pointer,
    AppendEvent @event,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(pointer);
    ArgumentNullException.ThrowIfNull(@event);

    // Implementation...
}

// ❌ Bad
public async Task<StreamEvent> AppendEventAsync(StreamPointer p, AppendEvent e)
{
    // No validation
    // Implementation...
}
```

### XML Documentation

All public APIs must have XML documentation:

```csharp
/// <summary>
/// Appends events to a stream with optimistic concurrency control.
/// </summary>
/// <param name="expectedVersion">The expected stream version for concurrency.</param>
/// <param name="events">The events to append.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>The appended events with their stream pointers.</returns>
/// <exception cref="StreamVersionConflictException">Thrown when version conflict occurs.</exception>
public async Task<IReadOnlyList<StreamEvent>> AppendAsync(
    StreamPointer expectedVersion,
    IReadOnlyList<AppendEvent> events,
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

### Nullable Reference Types

Always enable and respect nullable reference types:

```csharp
// ✅ Good
public async Task<Snapshot?> LoadSnapshotAsync(StreamIdentifier streamId)
{
    // May return null
}

// ❌ Bad
public async Task<Snapshot> LoadSnapshotAsync(StreamIdentifier streamId)
{
    // Signature says non-null but may return null
}
```

## Testing Guidelines

### Test Naming

Use descriptive test names that explain the scenario:

```csharp
// ✅ Good
[Fact]
public async Task AppendAsync_WhenVersionConflict_ThrowsStreamVersionConflictException()

// ❌ Bad
[Fact]
public async Task Test1()
```

### Test Structure

Follow the **Arrange-Act-Assert** pattern:

```csharp
[Fact]
public async Task AppendAsync_WhenValidEvent_StoresEventCorrectly()
{
    // Arrange
    var eventStore = CreateEventStore();
    var pointer = new StreamPointer(new StreamIdentifier("Order", "1"), 0);
    var @event = new AppendEvent(new OrderCreatedEvent(100m), null);

    // Act
    var result = await eventStore.AppendAsync(pointer, new[] { @event });

    // Assert
    Assert.Single(result);
    Assert.Equal(1, result[0].StreamPointer.Version);
}
```

### Test Coverage

- **Aim for high coverage** (80%+ code coverage)
- **Test happy paths** and **error scenarios**
- **Test edge cases** (null values, empty collections, boundary conditions)
- **Use in-memory database** for integration tests

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test /p:CollectCoverage=true

# Run specific test
dotnet test --filter "FullyQualifiedName~AppendAsync_WhenVersionConflict"
```

## Commit Message Guidelines

Follow [Conventional Commits](https://www.conventionalcommits.org/):

### Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Types

- **feat**: New feature
- **fix**: Bug fix
- **docs**: Documentation changes
- **style**: Code style changes (formatting, etc.)
- **refactor**: Code refactoring
- **test**: Adding or updating tests
- **chore**: Maintenance tasks

### Examples

```
feat(eventstore): add support for event metadata filtering

Added ability to filter events by metadata key/value pairs when loading
streams. This enables more efficient querying for correlation/causation
tracking scenarios.

Closes #123
```

```
fix(snapshots): correct snapshot versioning bug

Fixed issue where snapshots were being saved with incorrect version
numbers after multiple rapid updates.

Fixes #456
```

## Additional Resources

- [.NET API Guidelines](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/api-guidelines/README.md)
- [Entity Framework Core Documentation](https://docs.microsoft.com/en-us/ef/core/)
- [Event Sourcing Pattern](https://martinfowler.com/eaaDev/EventSourcing.html)

## Questions?

Feel free to open an issue with the `question` label or reach out to the maintainers.

---

Thank you for contributing! 🚀

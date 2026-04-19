# Migration Guide: StateRunner → AggregateRepository + AggregateCommandExecutor

This guide helps you migrate from the old `StateRunner` static utility methods to the new `AggregateRepository` + `AggregateCommandExecutor` architecture.

## What Changed?

### Old Architecture (v1.0)

```csharp
// Static utility methods with many parameters
var (state, version, events) = await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    command,
    registry,
    snapshotStore,  // optional
    metadata);      // optional
```

**Issues:**
- Static methods make testing harder
- Many parameters are repetitive
- Unclear responsibility allocation
- Folded events before persisting them (less safe)

### New Architecture (v1.1+)

```csharp
// DI-based components with clear responsibilities
services.AddTransient<AggregateCommandExecutor<TState, TCommand>>();

var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<TState, TCommand>>();
var (state, version, events) = await executor.ExecuteAsync(streamId, command, metadata);
```

**Benefits:**
- ✅ Instance-based (DI-friendly, testable)
- ✅ Clear separation of concerns (DDD Repository pattern)
- ✅ **Safer architecture: Persist events FIRST, derive state SECOND**
- ✅ Fewer parameters (dependencies injected)
- ✅ More extensible (override repository or executor)

## Step-by-Step Migration

### Step 1: Update Dependency Injection Configuration

**Before:**
```csharp
// You manually created instances
var folder = new OrderStateFolder(registry);
var decider = new OrderCommandDecider();
```

**After:**
```csharp
// Register services in DI container
services.AddSingleton<IStateFolder<OrderState>, OrderStateFolder>();
services.AddSingleton<ICommandDecider<OrderState, OrderCommand>, OrderCommandDecider>();
services.AddTransient<IAggregateRepository<OrderState>, AggregateRepository<OrderState>>();
services.AddTransient<AggregateCommandExecutor<OrderState, OrderCommand>>();
```

**Notes:**
- `IStateFolder` and `ICommandDecider` should be **Singleton** (stateless, reusable)
- `IAggregateRepository` and `AggregateCommandExecutor` should be **Transient** (per-operation)

### Step 2: Update Command Execution Code

**Before:**
```csharp
using var scope = serviceProvider.CreateScope();

var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
var registry = scope.ServiceProvider.GetRequiredService<ITypeMetadataRegistry>();
var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>(); // optional
var folder = new OrderStateFolder(registry);
var decider = new OrderCommandDecider();
var streamId = new StreamIdentifier("Order", "order-123");

var (state, version, events) = await StateRunner.ExecuteAsync(
    eventStore,
    folder,
    decider,
    streamId,
    command,
    registry,
    snapshotStore,
    metadata);
```

**After:**
```csharp
using var scope = serviceProvider.CreateScope();

var executor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<OrderState, OrderCommand>>();
var streamId = new StreamIdentifier("Order", "order-123");

var (state, version, events) = await executor.ExecuteAsync(
    streamId,
    command,
    metadata); // optional
```

**Notes:**
- `eventStore`, `snapshotStore`, `folder`, `decider`, and `registry` are now injected into `AggregateCommandExecutor` constructor
- Much cleaner call site - only pass what's specific to this command execution

### Step 3: Update Load State Code (if you used it directly)

**Before:**
```csharp
var (state, version) = await StateRunner.LoadStateAsync(
    eventStore,
    folder,
    streamId,
    registry,
    snapshotStore); // optional
```

**After:**
```csharp
var repository = scope.ServiceProvider.GetRequiredService<IAggregateRepository<OrderState>>();
var (state, version) = await repository.LoadStateAsync(streamId);
```

**Notes:**
- `AggregateRepository` encapsulates all loading logic (events + snapshots + validation)
- Snapshot store is automatically used if registered in DI

### Step 4: Update Tests

**Before (testing with StateRunner):**
```csharp
[Fact]
public async Task ExecuteAsync_WithValidCommand_AppendsEvents()
{
    // Arrange
    var eventStore = new InMemoryEventStore();
    var folder = new OrderStateFolder(registry);
    var decider = new OrderCommandDecider();
    var streamId = new StreamIdentifier("Order", "order-123");
    var command = new PlaceOrder("order-123");

    // Act
    var (state, version, events) = await StateRunner.ExecuteAsync(
        eventStore,
        folder,
        decider,
        streamId,
        command,
        registry);

    // Assert
    Assert.Single(events);
    Assert.Equal("order-123", state.OrderId);
}
```

**After (testing with AggregateCommandExecutor):**
```csharp
[Fact]
public async Task ExecuteAsync_WithValidCommand_AppendsEvents()
{
    // Arrange
    var eventStore = new InMemoryEventStore();
    var folder = new OrderStateFolder(registry);
    var decider = new OrderCommandDecider();
    var repository = new AggregateRepository<OrderState>(eventStore, folder, null); // no snapshot store
    var executor = new AggregateCommandExecutor<OrderState, OrderCommand>(repository, decider, registry);
    var streamId = new StreamIdentifier("Order", "order-123");
    var command = new PlaceOrder("order-123");

    // Act
    var (state, version, events) = await executor.ExecuteAsync(streamId, command);

    // Assert
    Assert.Single(events);
    Assert.Equal("order-123", state.OrderId);
}
```

**Even Better (with DI in tests):**
```csharp
[Fact]
public async Task ExecuteAsync_WithValidCommand_AppendsEvents()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSingleton<IEventStore>(new InMemoryEventStore());
    services.AddSingleton<ITypeMetadataRegistry>(registry);
    services.AddSingleton<IStateFolder<OrderState>, OrderStateFolder>();
    services.AddSingleton<ICommandDecider<OrderState, OrderCommand>, OrderCommandDecider>();
    services.AddTransient<IAggregateRepository<OrderState>, AggregateRepository<OrderState>>();
    services.AddTransient<AggregateCommandExecutor<OrderState, OrderCommand>>();

    using var scope = services.BuildServiceProvider().CreateScope();
    var executor = scope.ServiceProvider.GetRequiredService<AggregateCommandExecutor<OrderState, OrderCommand>>();
    var streamId = new StreamIdentifier("Order", "order-123");
    var command = new PlaceOrder("order-123");

    // Act
    var (state, version, events) = await executor.ExecuteAsync(streamId, command);

    // Assert
    Assert.Single(events);
    Assert.Equal("order-123", state.OrderId);
}
```

## Complete Example: Before & After

### Before (v1.0)

```csharp
// Program.cs or Startup.cs - minimal DI
services.AddEventStoreSqlServer(connectionString, assemblies);

// In your command handler / API controller
public class OrderController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;

    public OrderController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    [HttpPost("orders/{orderId}/approve")]
    public async Task<IActionResult> ApproveOrder(string orderId, [FromBody] ApproveOrderRequest request)
    {
        using var scope = _serviceProvider.CreateScope();

        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
        var registry = scope.ServiceProvider.GetRequiredService<ITypeMetadataRegistry>();
        var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
        var folder = new OrderStateFolder(registry);
        var decider = new OrderCommandDecider();
        var streamId = new StreamIdentifier("Order", orderId);

        var command = new ApproveOrder(orderId);

        try
        {
            var (state, version, events) = await StateRunner.ExecuteAsync(
                eventStore,
                folder,
                decider,
                streamId,
                command,
                registry,
                snapshotStore,
                [new AppendMetadata("CorrelationId", request.CorrelationId)]);

            return Ok(new { version, eventsCount = events.Count });
        }
        catch (StreamVersionConflictException)
        {
            return Conflict("Order was modified by another request");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

### After (v1.1+)

```csharp
// Program.cs or Startup.cs - comprehensive DI
services.AddEventStoreSqlServer(connectionString, assemblies);

// Register aggregate components
services.AddSingleton<IStateFolder<OrderState>, OrderStateFolder>();
services.AddSingleton<ICommandDecider<OrderState, OrderCommand>, OrderCommandDecider>();
services.AddTransient<IAggregateRepository<OrderState>, AggregateRepository<OrderState>>();
services.AddTransient<AggregateCommandExecutor<OrderState, OrderCommand>>();

// In your command handler / API controller
public class OrderController : ControllerBase
{
    private readonly AggregateCommandExecutor<OrderState, OrderCommand> _executor;

    public OrderController(AggregateCommandExecutor<OrderState, OrderCommand> executor)
    {
        _executor = executor;
    }

    [HttpPost("orders/{orderId}/approve")]
    public async Task<IActionResult> ApproveOrder(string orderId, [FromBody] ApproveOrderRequest request)
    {
        var streamId = new StreamIdentifier("Order", orderId);
        var command = new ApproveOrder(orderId);

        try
        {
            var (state, version, events) = await _executor.ExecuteAsync(
                streamId,
                command,
                [new AppendMetadata("CorrelationId", request.CorrelationId)]);

            return Ok(new { version, eventsCount = events.Count });
        }
        catch (StreamVersionConflictException)
        {
            return Conflict("Order was modified by another request");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

**Benefits of new approach:**
- ✅ Controller constructor is simpler (one dependency instead of service locator)
- ✅ Better testability (can inject mock executor)
- ✅ Less boilerplate in action method
- ✅ Dependencies configured once at startup, not repeated per request
- ✅ Follows ASP.NET Core best practices (constructor injection)

## Breaking Changes

### 1. StateRunner class removed

**Before:**
```csharp
await StateRunner.ExecuteAsync(...);
await StateRunner.LoadStateAsync(...);
```

**After:**
```csharp
var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<TState, TCommand>>();
await executor.ExecuteAsync(...);

var repository = serviceProvider.GetRequiredService<IAggregateRepository<TState>>();
await repository.LoadStateAsync(...);
```

### 2. Different execution order

**Before:** Load → Decide → Fold → Persist (❌ less safe)

**After:** Load → Decide → **Persist → Fold** (✅ safer - events stored first)

**Impact:** None for users (behavior is the same), but infrastructure is safer

### 3. Snapshot store is optional via DI

**Before:**
```csharp
await StateRunner.ExecuteAsync(..., snapshotStore: null); // explicitly pass null
```

**After:**
```csharp
// Don't register ISnapshotStore in DI
// OR register as null
services.AddSingleton<ISnapshotStore>(sp => null);
```

## Common Migration Patterns

### Pattern 1: Simple Command Execution

**Before:**
```csharp
var (state, version, events) = await StateRunner.ExecuteAsync(
    eventStore, folder, decider, streamId, command, registry);
```

**After:**
```csharp
var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<TState, TCommand>>();
var (state, version, events) = await executor.ExecuteAsync(streamId, command);
```

### Pattern 2: Command with Metadata

**Before:**
```csharp
var (state, version, events) = await StateRunner.ExecuteAsync(
    eventStore, folder, decider, streamId, command, registry, null, metadata);
```

**After:**
```csharp
var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<TState, TCommand>>();
var (state, version, events) = await executor.ExecuteAsync(streamId, command, metadata);
```

### Pattern 3: Command with Snapshots

**Before:**
```csharp
var (state, version, events) = await StateRunner.ExecuteAsync(
    eventStore, folder, decider, streamId, command, registry, snapshotStore);
```

**After:**
```csharp
// Configure snapshot store in DI (once at startup)
services.AddSingleton<ISnapshotStore>(/* your snapshot store */);

// Execute command (snapshot store automatically used)
var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<TState, TCommand>>();
var (state, version, events) = await executor.ExecuteAsync(streamId, command);
```

### Pattern 4: Expected Version Validation

**Before:**
```csharp
[Command("Order", ExpectedVersionKey = "ExpectedVersion")]
public record ApproveOrder(string OrderId);

var metadata = new[] { new AppendMetadata("ExpectedVersion", expectedVersion) };
var (state, version, events) = await StateRunner.ExecuteAsync(
    eventStore, folder, decider, streamId, command, registry, null, metadata);
```

**After:**
```csharp
[Command("Order", ExpectedVersionKey = "ExpectedVersion")]
public record ApproveOrder(string OrderId);

var metadata = new[] { new AppendMetadata("ExpectedVersion", expectedVersion) };
var executor = serviceProvider.GetRequiredService<AggregateCommandExecutor<TState, TCommand>>();
var (state, version, events) = await executor.ExecuteAsync(streamId, command, metadata);
```

**Note:** Expected version handling is identical, just moved from StateRunner to AggregateCommandExecutor

## Testing Migration

### Unit Tests (Domain Logic) - No Changes Required

Your tests for `StateFolder` and `CommandDecider` don't need to change:

```csharp
[Fact]
public void StateFolder_When_OrderPlaced_SetsOrderId()
{
    var folder = new OrderStateFolder(registry);
    var state = folder.InitialState();
    var @event = new OrderPlaced("order-123", DateTime.UtcNow);

    var newState = folder.Apply(state, @event);

    Assert.Equal("order-123", newState.OrderId);
}
```

These remain pure unit tests of domain logic.

### Integration Tests (Full Workflow) - Minor Changes

**Before:**
```csharp
[Fact]
public async Task ExecuteAsync_AppendsEventsToStore()
{
    var eventStore = new InMemoryEventStore();
    var folder = new OrderStateFolder(registry);
    var decider = new OrderCommandDecider();
    var streamId = new StreamIdentifier("Order", "order-123");

    var (state, version, events) = await StateRunner.ExecuteAsync(
        eventStore, folder, decider, streamId, new PlaceOrder("order-123"), registry);

    Assert.Single(events);
}
```

**After:**
```csharp
[Fact]
public async Task ExecuteAsync_AppendsEventsToStore()
{
    var eventStore = new InMemoryEventStore();
    var folder = new OrderStateFolder(registry);
    var decider = new OrderCommandDecider();
    var repository = new AggregateRepository<OrderState>(eventStore, folder, null);
    var executor = new AggregateCommandExecutor<OrderState, OrderCommand>(repository, decider, registry);
    var streamId = new StreamIdentifier("Order", "order-123");

    var (state, version, events) = await executor.ExecuteAsync(streamId, new PlaceOrder("order-123"));

    Assert.Single(events);
}
```

**Even better with a test helper:**
```csharp
// Test helper class
public class AggregateTestHarness<TState, TCommand>
{
    private readonly AggregateCommandExecutor<TState, TCommand> _executor;

    public AggregateTestHarness(
        IStateFolder<TState> folder,
        ICommandDecider<TState, TCommand> decider,
        ITypeMetadataRegistry registry,
        ISnapshotStore? snapshotStore = null)
    {
        var eventStore = new InMemoryEventStore();
        var repository = new AggregateRepository<TState>(eventStore, folder, snapshotStore);
        _executor = new AggregateCommandExecutor<TState, TCommand>(repository, decider, registry);
    }

    public Task<(TState, long, IReadOnlyList<StreamEvent>)> ExecuteAsync(
        StreamIdentifier streamId,
        TCommand command,
        IReadOnlyList<AppendMetadata>? metadata = null) =>
        _executor.ExecuteAsync(streamId, command, metadata);
}

// In tests
[Fact]
public async Task ExecuteAsync_AppendsEventsToStore()
{
    var harness = new AggregateTestHarness<OrderState, OrderCommand>(
        new OrderStateFolder(registry),
        new OrderCommandDecider(),
        registry);

    var streamId = new StreamIdentifier("Order", "order-123");
    var (state, version, events) = await harness.ExecuteAsync(streamId, new PlaceOrder("order-123"));

    Assert.Single(events);
}
```

## FAQ

### Q: Why was this change made?

**A:** Three main reasons:
1. **Safer architecture**: Persisting events before folding them ensures events (source of truth) are safe even if folding/snapshotting fails
2. **Better testability**: Instance-based components are easier to mock and test than static methods
3. **Clearer responsibilities**: DDD Repository pattern clarifies who owns what (persistence vs. orchestration vs. domain logic)

### Q: Do I need to rewrite my StateFolder or CommandDecider?

**A:** No! Your domain logic (folders and deciders) remains unchanged. Only the infrastructure (how you load/execute) changes.

### Q: What if I don't use DI?

**A:** You can still use the new architecture by creating instances manually:

```csharp
var eventStore = /* your event store */;
var folder = new OrderStateFolder(registry);
var decider = new OrderCommandDecider();
var repository = new AggregateRepository<OrderState>(eventStore, folder, snapshotStore: null);
var executor = new AggregateCommandExecutor<OrderState, OrderCommand>(repository, decider, registry);

var (state, version, events) = await executor.ExecuteAsync(streamId, command);
```

But we **strongly recommend** using DI for better testability and following .NET best practices.

### Q: Can I still test my aggregates without setting up all this infrastructure?

**A:** Yes! Test your domain logic (StateFolder, CommandDecider) in isolation:

```csharp
[Fact]
public void CommandDecider_ValidatesBusinessRules()
{
    var decider = new OrderCommandDecider();
    var state = new OrderState { Status = OrderStatus.Shipped };
    var command = new CancelOrder();

    var ex = Assert.Throws<InvalidOperationException>(
        () => decider.Execute(state, command));

    Assert.Contains("cannot cancel shipped order", ex.Message);
}
```

No infrastructure needed - just pure unit tests.

### Q: What about performance?

**A:** Performance is **the same or better**:
- Same number of I/O operations (read events, write events, optional snapshot)
- **Persist-then-fold** is safer, not slower
- DI overhead is negligible (constructor injection happens once per request)
- Snapshots work the same way (automatic at configured intervals)

## Need Help?

- Review the [ARCHITECTURE.md](ARCHITECTURE.md) document for detailed architectural explanations
- Check the [README.md](README.md) for usage examples
- Look at the test suite (`Rickten.Aggregator.Tests`) for working examples
- Open an issue on GitHub if you encounter migration problems

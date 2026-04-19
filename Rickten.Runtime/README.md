# Rickten.Runtime

**Runtime host for Rickten reactions.** Provides background services for continuous reaction execution in .NET Generic Host applications with DI, cancellation, logging, and failure handling.

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![C# 14.0](https://img.shields.io/badge/C%23-14.0-blue)](https://docs.microsoft.com/en-us/dotnet/csharp/)

## ?? Installation

```bash
dotnet add package Rickten.Runtime
```

## ?? What is Rickten.Runtime?

Rickten.Runtime is the **first way to actually run reactions**. Before this package, applications had to manually call `ReactionRunner.CatchUpAsync` in custom loops. This package wraps catch-up in a `BackgroundService` that:

- ? Runs reactions continuously on a configurable polling interval
- ? Uses scoped service providers per catch-up iteration  
- ? Respects cancellation and graceful shutdown
- ? Logs failures and continues on the next interval (no permanent crashes)
- ? Integrates with .NET Generic Host (console apps, worker services, ASP.NET Core, etc.)

**This first release focuses on reactions only.** Hosted projection runtime, delayed reactions, recurring actions, cron scheduling, distributed leases, multi-instance orchestration, and exactly-once guarantees are **future work**.

## ?? Quick Start

### 1. Configure Runtime

```csharp
using Microsoft.Extensions.Hosting;
using Rickten.Runtime;

var builder = Host.CreateApplicationBuilder(args);

// Add Rickten event store and reactor
builder.Services.AddEventStoreSqlServer(
    connectionString,
    typeof(MyEvent).Assembly,
    typeof(MyReaction).Assembly);

builder.Services.AddReactions(typeof(MyReaction).Assembly);

// Configure the Rickten runtime
builder.Services.AddRicktenRuntime(options =>
{
    options.DefaultPollingInterval = TimeSpan.FromSeconds(5);
});

// Add hosted reactions (simple!)
builder.Services.AddHostedReaction<MyReaction, MyState, MyView, MyCommand>();

var host = builder.Build();
await host.RunAsync();
```

### 2. Define Your Reaction

Reactions can specify their polling interval in the attribute:

```csharp
using Rickten.Reactor;

// Polls every 2 seconds
[Reaction("MyReaction", 
    EventTypes = new[] { "Order.Placed.v1" },
    PollingIntervalMilliseconds = 2000)]
public class MyReaction : Reaction<MyView, MyCommand>
{
    public MyReaction(ITypeMetadataRegistry registry) : base(registry) { }

    public override IProjection<MyView> Projection => new MyProjection();

    protected override IEnumerable<StreamIdentifier> SelectStreams(MyView view, StreamEvent trigger)
    {
        if (trigger.Event is OrderPlaced evt)
            yield return new StreamIdentifier("Order", evt.OrderId);
    }

    protected override MyCommand BuildCommand(StreamIdentifier stream, MyView view, StreamEvent trigger)
        => new MyCommand(stream.Identifier);
}
```

### Polling Interval Resolution

The polling interval is determined in this order:

1. **Parameter override** when calling `AddHostedReaction`
2. **ReactionAttribute.PollingIntervalMilliseconds** on the reaction class  
3. **RicktenRuntimeOptions.DefaultPollingInterval** from runtime configuration

```csharp
// Uses attribute (2000ms) or runtime default
services.AddHostedReaction<MyReaction, TState, TView, TCommand>();

// Override: uses 1 second regardless of attribute
services.AddHostedReaction<MyReaction, TState, TView, TCommand>(
    pollingInterval: TimeSpan.FromSeconds(1));
```

## ?? API Reference

### AddRicktenRuntime

```csharp
// Default options (5-second polling interval)
services.AddRicktenRuntime();

// Custom default
services.AddRicktenRuntime(options =>
{
    options.DefaultPollingInterval = TimeSpan.FromSeconds(10);
});
```

### AddHostedReaction

```csharp
// Use attribute or runtime default
services.AddHostedReaction<MyReaction, MyState, MyView, MyCommand>();

// Override polling interval
services.AddHostedReaction<FastReaction, MyState, MyView, MyCommand>(
    TimeSpan.FromSeconds(1));
```

## ?? Configuration

### RicktenRuntimeOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultPollingInterval` | `TimeSpan` | `5 seconds` | Fallback polling interval when not specified on attribute or parameter |

### ReactionAttribute

| Property | Type | Description |
|----------|------|-------------|
| `PollingIntervalMilliseconds` | `int?` | Polling interval for this reaction when hosted (milliseconds) |

## ?? How It Works

1. **Startup:** All `ReactionHostedService` instances start as `BackgroundService` implementations
2. **Loop:** Each service repeatedly:
   - Creates a new service scope
   - Resolves dependencies (IEventStore, reaction, executor, etc.)
   - Calls `ReactionRunner.CatchUpAsync`
   - Disposes the scope
   - Waits for the polling interval
3. **Failure Handling:** Exceptions are logged; service continues on next interval
4. **Shutdown:** Cancellation tokens signal graceful stop

## ?? What This Does NOT Include

This is the **first small slice**. Not included (future work):

- ? Hosted projection runtime
- ? Delayed/scheduled reactions
- ? Cron scheduling
- ? Distributed leases or multi-instance coordination
- ? Exactly-once guarantees (reactions are at-least-once; ensure idempotent commands)
- ? Backpressure or throttling
- ? Health checks

## ?? Additional Documentation

- [Rickten.Reactor README](../Rickten.Reactor/README.md)
- [Rickten.EventStore README](../README.md)
- [API Documentation](../docs/API.md)

## ?? License

MIT License - see [LICENSE](../LICENSE) for details.

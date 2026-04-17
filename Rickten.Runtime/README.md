# Rickten.Runtime

Generic execution layer for Rickten background processes.

Provides hosted service infrastructure for running reactions and other Rickten components as long-lived background workers in ASP.NET Core and other hosting environments.

## Installation

```bash
dotnet add package Rickten.Runtime
```

## Features

- **Reaction Runtime**: Execute reactions as background services with configurable polling intervals
- **Error Handling**: Configurable stop-on-error or retry-with-delay behavior
- **Scoped Dependencies**: Fresh DI scope per execution pass for proper resource management
- **Enable/Disable**: Runtime control over reaction execution

## Quick Start

```csharp
builder.Services.AddRicktenRuntime(runtime =>
{
    runtime.AddReaction<
        MembershipDefinitionChangedReaction,
        MembershipState,
        MembershipDefinitionView,
        RecalculateMembershipCommand>(options =>
    {
        options.PollingInterval = TimeSpan.FromSeconds(5);
        options.Enabled = true;
        options.ErrorBehavior = RicktenRuntimeErrorBehavior.Stop;
    });
});
```

## Documentation

For full documentation, visit the [Rickten GitHub repository](https://github.com/zebrai2/rickten.eventstore).

## License

MIT

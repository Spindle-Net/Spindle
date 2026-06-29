# Spindle.Hosting

`Spindle.Hosting` wires the local Spindle runtime into the .NET generic host.
It gives you a background worker that advances persisted flow instances over time instead of running every flow to completion inside the caller.

This package is for the local MVP runtime. It uses persistence as the source of truth, polls runnable instances, executes local steps, fires due timers, and replays flows after progress is made.

## Register Hosting

Register a store, register your flows, then add the worker.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spindle;
using Spindle.Abstractions.Core;
using Spindle.Hosting;
using Spindle.Persistence;
using Spindle.Persistence.InMemory;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ISpindleStore, InMemorySpindleStore>();

builder.Services.AddSpindleFlow<MyFlow, MyRequest, MyResult>(
    new FlowName("my-flow"));

builder.Services.AddSpindleWorker(options =>
{
    options.PollInterval = TimeSpan.FromMilliseconds(250);
    options.MaxConcurrentFlowInstances = 4;
    options.MaxStepsPerFlowPerTick = 1;
    options.WorkerId = "worker-1";
});

await builder.Build().RunAsync();
```

`AddSpindleWorker` registers:

- `RuntimeSpindleRuntime`
- `ISpindleRuntime`
- `ISpindleRuntimePump`
- `SpindleWorkerHostedService`

## Start Flows From Another Service

Any hosted service, controller, message consumer, or job can inject `ISpindleRuntime` and schedule a flow.

```csharp
public sealed class TextConsumerService(
    ISpindleRuntime runtime)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await runtime.StartAsync<TextRequest, TextResult>(
            new FlowName("text-transform"),
            new TextRequest("Hello Durable Workflows"),
            new StartFlowOptions { IdempotencyKey = "text-1" },
            stoppingToken);
    }
}
```

The caller only creates the instance and performs the first expansion. The hosted Spindle worker later advances the instance by executing ready local steps, firing due timers, and replaying the flow.

## Step Handlers

Register step handlers when flow code uses `ctx.StepHandler(...)`.

```csharp
builder.Services.AddSpindleStepHandler<MyHandler, MyHandlerRequest, MyHandlerResult>(
    new StepHandlerId("my-handler"));
```

For this MVP, handlers are local process handlers. Queue-only workers and remote handlers are future work.

## Options

`SpindleHostOptions` controls the local worker loop.

| Option | Default | Purpose |
| --- | --- | --- |
| `PollInterval` | `250ms` | Delay when no progress is made. |
| `MaxFlowInstancesPerTick` | `100` | Maximum runnable instances read per worker tick. |
| `MaxStepsPerFlowPerTick` | `1` | Maximum ready local steps executed for one flow advancement. |
| `MaxConcurrentFlowInstances` | CPU count | Maximum flow instances advanced concurrently. |
| `LeaseDuration` | `30s` | Local step lease duration. |
| `WorkerId` | machine-based | Worker identity recorded on attempts and leases. |

Keeping `MaxStepsPerFlowPerTick` small is useful for long-running services because one flow cannot drain all ready work in one pump cycle.

## Example

Run the hosting example:

```bash
dotnet run --project examples/Spindle.Example.Hosting/Spindle.Example.Hosting.csproj
```

The example has two hosted services:

- `SpindleWorkerHostedService`, registered by `AddSpindleWorker`, which advances durable flow instances.
- `TextConsumerService`, which consumes text from a small in-memory inbox and starts one flow per text value.

The flow transforms each string into upper, lower, and camel-case outputs.

## Current Limitations

- The in-memory store is for tests and local examples only.
- Only local `Immediate` and `LocalWorker` step execution is supported.
- Queue dispatch, RabbitMQ, EF Core, remote flows, dashboards, source generators, and analyzers are not part of this package yet.
- The worker polls persistence. Future transport-backed wakeups can reduce polling and support distributed queue workers.

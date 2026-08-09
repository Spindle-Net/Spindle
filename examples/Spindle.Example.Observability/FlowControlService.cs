using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Example.Observability;
using Spindle.Persistence;
using Spindle.Persistence.Nodes;

namespace Spindle.Example.Hosting;

public sealed class FlowControlService(
    ISpindleRuntime runtime,
    ISpindleStore store,
    IHostApplicationLifetime lifetime)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var pending = new HashSet<FlowInstanceId>();
        var index = 0;
        var randomId = Guid.NewGuid();

        for (int i = 1; i <= 1; i++)
        {
            var handle = await runtime.EnqueueAsync<Unit, Unit>(
                    UnitDummyFlow.Name,
                    Unit.Value,
                    new StartFlowOptions { IdempotencyKey = $"unit-example-{index++}-{randomId}" },
                    stoppingToken)
                .ConfigureAwait(false);

            pending.Add(handle.InstanceId);
            Console.WriteLine($"Queued: {handle.InstanceId}");
        }

        while (pending.Count > 0 && !stoppingToken.IsCancellationRequested)
        {
            foreach (var instanceId in pending.ToArray())
            {
                var instance = await store.FlowInstances
                    .GetAsync(instanceId, stoppingToken)
                    .ConfigureAwait(false);

                if (instance?.Status == FlowInstanceStatus.Completed)
                {
                    await PrintCompletedAsync(instanceId, stoppingToken)
                        .ConfigureAwait(false);
                    pending.Remove(instanceId);
                    continue;
                }

                if (instance?.Status is FlowInstanceStatus.Failed
                    or FlowInstanceStatus.Cancelled
                    or FlowInstanceStatus.TimedOut)
                {
                    Console.WriteLine($"Failed: {instanceId} ({instance.Status})");
                    pending.Remove(instanceId);
                }
            }

            if (pending.Count > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken)
                    .ConfigureAwait(false);
            }
        }

        lifetime.StopApplication();
    }

    private async ValueTask PrintCompletedAsync(
        FlowInstanceId instanceId,
        CancellationToken cancellationToken)
    {
        var instance = await store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        if (instance?.Result is null)
        {
            Console.WriteLine($"Completed: {instanceId}");
            return;
        }


        Console.WriteLine();
        Console.WriteLine($"Completed:   {instanceId}");
        Console.WriteLine($"Created At:  {instance.CreatedAt}");
        Console.WriteLine($"Finished At: {instance.CompletedAt}");
        Console.WriteLine($"Time:        {instance.CompletedAt - instance.CreatedAt}");
    }
}

using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence;
using Spindle.Persistence.Steps;

namespace Spindle.Example.Hosting;

public sealed class TextConsumerService(
    ChannelReader<string> textInbox,
    ISpindleRuntime runtime,
    ISpindleStore store,
    ISpindleSerializer serializer,
    IHostApplicationLifetime lifetime)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var pending = new Dictionary<FlowInstanceId, string>();
        var index = 0;

        await foreach (var text in textInbox.ReadAllAsync(stoppingToken))
        {
            var handle = await runtime.StartAsync<TextTransformRequest, TextTransformResult>(
                    TextTransformFlow.Name,
                    new TextTransformRequest(text),
                    new StartFlowOptions { IdempotencyKey = $"hosting-example-{index++}" },
                    stoppingToken)
                .ConfigureAwait(false);

            pending[handle.InstanceId] = text;
            Console.WriteLine($"Queued: {handle.InstanceId} <- {text}");
        }

        while (pending.Count > 0 && !stoppingToken.IsCancellationRequested)
        {
            foreach (var (instanceId, text) in pending.ToArray())
            {
                var snapshot = await runtime
                    .GetInstanceAsync(instanceId, stoppingToken)
                    .ConfigureAwait(false);

                if (snapshot?.Status == FlowInstanceStatus.Completed)
                {
                    await PrintCompletedAsync(instanceId, text, stoppingToken)
                        .ConfigureAwait(false);
                    pending.Remove(instanceId);
                    continue;
                }

                if (snapshot?.Status is FlowInstanceStatus.Failed
                    or FlowInstanceStatus.Cancelled
                    or FlowInstanceStatus.TimedOut)
                {
                    Console.WriteLine($"Failed: {instanceId} ({snapshot.Status}) <- {text}");
                    pending.Remove(instanceId);
                    continue;
                }

                Console.WriteLine($"Pending: {instanceId} ({snapshot?.Status}) <- {text}");
                if (snapshot is not null)
                {
                    foreach (var step in snapshot.Steps)
                    {
                        Console.WriteLine($"  Step: {step.StepId} ({step.Status}) - {step.CompletedAt?.ToString("o") ?? "not completed"}");
                    }
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
        string input,
        CancellationToken cancellationToken)
    {
        var instance = await store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        if (instance?.Result is null)
        {
            Console.WriteLine($"Completed: {instanceId} <- {input}");
            return;
        }

        var result = serializer.Deserialize<TextTransformResult>(instance.Result);

        Console.WriteLine();
        Console.WriteLine($"Completed: {instanceId}");
        Console.WriteLine($"Input:     {input}");
        Console.WriteLine($"Upper:     {result.Upper}");
        Console.WriteLine($"Lower:     {result.Lower}");
        Console.WriteLine($"CamelCase: {result.CamelCase}");
        Console.WriteLine($"Value:     {result.Value}");
    }
}

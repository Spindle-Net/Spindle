using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;

namespace Spindle;

internal sealed class StepExecutor(
    ISpindleStore store,
    StepScheduler scheduler,
    ISpindleSerializer serializer,
    TimeProvider timeProvider,
    TimeSpan leaseDuration,
    IServiceProvider services,
    ILogger? logger,
    string workerId)
{

    private List<IStepExecutor> _executors =
    [
        new LocalStepExecutor(
            store,
            scheduler,
            serializer,
            timeProvider,
            leaseDuration,
            services,
            logger,
            workerId)
    ];


    public async ValueTask<int> ExecuteReadyStepsAsync(
        FlowExecutionSession session,
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        var steps = await store.Steps
            .GetByFlowInstanceAsync(session.FlowInstanceId, cancellationToken)
            .ConfigureAwait(false);

        var executed = 0;
        var tasks = new List<Task>();

        foreach (var step in steps
            .Where(step => step.Status == StepStatus.Ready)
            .OrderBy(step => step.CreatedAt)
            .ThenBy(step => step.StepId.Value, StringComparer.Ordinal)
            .Take(maxCount))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var executor = _executors.FirstOrDefault(e => e.SupportsDispatchMode(step.DispatchMode));
            if (executor != null)
            {
                var task = executor.ExecuteAsync(session, step, cancellationToken)
                    .ContinueWith(task =>
                    {
                        if (task is { IsCompletedSuccessfully: true, Result: true })
                        {
                            Interlocked.Increment(ref executed);
                        }
                        else if (task.IsFaulted)
                        {
                            logger?.LogError(
                                task.Exception,
                                "Error executing step {StepId} for flow instance {FlowInstanceId}",
                                step.StepId,
                                step.FlowInstanceId);
                        }
                    }, cancellationToken);
                tasks.Add(task);
            }
        }

        await Task.WhenAll(tasks);

        return executed;
    }
}

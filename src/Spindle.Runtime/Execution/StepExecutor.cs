using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;
using Spindle.Persistence.Steps;

namespace Spindle;

internal sealed class StepExecutor(
    ISpindleStore store,
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
        var executed = 0;
        var attempted = new HashSet<StepId>();
        var steps = session
            .GetStepsSnapshot()
            .ToDictionary(step => step.StepId);

        while (attempted.Count < maxCount)
        {
            var remaining = maxCount - attempted.Count;
            var readySteps = steps.Values
                .Where(step => step.Status == StepStatus.Ready && !attempted.Contains(step.StepId))
                .OrderBy(step => step.CreatedAt)
                .ThenBy(step => step.StepId.Value, StringComparer.Ordinal)
                .Take(remaining)
                .ToArray();

            if (readySteps.Length == 0)
            {
                break;
            }

            foreach (var step in readySteps)
            {
                attempted.Add(step.StepId);
            }

            var tasks = new List<Task<(StepInstanceRecord Step, StepExecutionResult Result)>>();

            foreach (var step in readySteps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var executor = _executors.FirstOrDefault(e => e.SupportsDispatchMode(step.DispatchMode));
                if (executor != null)
                {
                    var task = ExecuteStepAsync(executor, session, step, cancellationToken);
                    tasks.Add(task);
                }
            }

            foreach (var (step, result) in await Task.WhenAll(tasks).ConfigureAwait(false))
            {
                if (!result.Executed)
                {
                    continue;
                }

                executed++;

                if (!result.Completed)
                {
                    steps[step.StepId] = step with { Status = StepStatus.Failed };
                    session.UpsertStep(steps[step.StepId]);
                    continue;
                }

                steps[step.StepId] = step with { Status = StepStatus.Completed };
                session.UpsertStep(steps[step.StepId]);

                foreach (var dependent in steps.Values
                             .Where(candidate =>
                                 (candidate.Status is StepStatus.Pending or StepStatus.Waiting) &&
                                 candidate.Dependencies.All(dependency =>
                                     steps.TryGetValue(dependency, out var prerequisite) &&
                                     prerequisite.Status == StepStatus.Completed))
                             .ToArray())
                {
                    steps[dependent.StepId] = dependent with { Status = StepStatus.Ready };
                    session.UpsertStep(steps[dependent.StepId]);
                }
            }
        }

        return executed;
    }

    private async Task<(StepInstanceRecord Step, StepExecutionResult Result)> ExecuteStepAsync(
        IStepExecutor executor,
        FlowExecutionSession session,
        StepInstanceRecord step,
        CancellationToken cancellationToken)
    {
        try
        {
            return (step, await executor
                .ExecuteAsync(session, step, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception,
                "Error executing step {StepId} for flow instance {FlowInstanceId}",
                step.StepId,
                step.FlowInstanceId);
            return (step, StepExecutionResult.Failed);
        }
    }
}

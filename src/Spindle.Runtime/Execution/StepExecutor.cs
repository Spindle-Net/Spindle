using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;
using Spindle.Persistence.Nodes;
using Spindle.Runtime;
using System.Diagnostics;

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
        using var mainActivity = Telemetry.ActivitySource.StartActivity($"ExecuteReadySteps - {session.FlowInstanceId.Value}");
        mainActivity?.SetTag("spindle.worker-id", workerId);

        var executed = 0;
        var attempted = new HashSet<NodeId>();
        var steps = session
            .GetNodesSnapshot()
            .ToDictionary(step => step.NodeId);

        while (attempted.Count < maxCount)
        {
            var remaining = maxCount - attempted.Count;
            var readySteps = steps.Values
                .Where(step =>
                    (step.Kind is NodeKind.Step or NodeKind.ConditionWait) &&
                    step.Status == NodeStatus.Ready &&
                    !attempted.Contains(step.NodeId))
                .OrderBy(step => step.CreatedAt)
                .ThenBy(step => step.NodeId.Value, StringComparer.Ordinal)
                .Take(remaining)
                .ToArray();

            if (readySteps.Length == 0)
            {
                break;
            }

            foreach (var step in readySteps)
            {
                attempted.Add(step.NodeId);
            }

            var tasks = new List<Task<(NodeInstanceRecord Step, StepExecutionResult Result)>>();

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

                steps[step.NodeId] = step with { Status = result.Status };
                session.UpsertNode(steps[step.NodeId]);

                if (result.Status != NodeStatus.Completed)
                {
                    continue;
                }

                foreach (var dependent in steps.Values
                             .Where(candidate =>
                                 (candidate.Kind is NodeKind.Step or NodeKind.ConditionWait) &&
                                 candidate.Status == NodeStatus.Pending &&
                                 candidate.Dependencies.All(dependency =>
                                     steps.TryGetValue(dependency, out var prerequisite) &&
                                     prerequisite.Status == NodeStatus.Completed))
                             .ToArray())
                {
                    steps[dependent.NodeId] = dependent with { Status = NodeStatus.Ready };
                    session.UpsertNode(steps[dependent.NodeId]);
                }
            }
        }

        return executed;
    }

    private async Task<(NodeInstanceRecord Step, StepExecutionResult Result)> ExecuteStepAsync(
        IStepExecutor executor,
        FlowExecutionSession session,
        NodeInstanceRecord step,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity($"ExecuteStep - {step.NodeId.Value}");
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
                "Error executing step {NodeId} for flow instance {FlowInstanceId}",
                step.NodeId,
                step.FlowInstanceId);
            return (step, StepExecutionResult.Failed);
        }
    }
}

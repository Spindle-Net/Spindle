using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence;
using Spindle.Persistence.Nodes;

namespace Spindle;

internal sealed class BarrierProcessor(
    ISpindleStore store,
    ISpindleSerializer serializer,
    TimeProvider timeProvider)
{
    public async ValueTask<int> ProcessAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        var processed = 0;

        while (true)
        {
            var changed = await store.ExecuteAsync(
                    async (session, storeCancellationToken) =>
                    {
                        var nodes = await session.Nodes
                            .GetByFlowInstanceAsync(flowInstanceId, storeCancellationToken)
                            .ConfigureAwait(false);
                        var byId = nodes.ToDictionary(node => node.NodeId);
                        var now = timeProvider.GetUtcNow();

                        foreach (var barrier in nodes.Where(IsPendingBarrier))
                        {
                            var dependencies = barrier.Dependencies
                                .Select(id => byId.GetValueOrDefault(id))
                                .ToArray();

                            if (dependencies.Any(dependency => dependency is null))
                            {
                                continue;
                            }

                            var inputs = dependencies.Cast<NodeInstanceRecord>().ToArray();
                            var evaluation = Evaluate(barrier, inputs);
                            if (evaluation is null)
                            {
                                continue;
                            }

                            if (evaluation.Error is not null)
                            {
                                await session.Nodes.MarkFailedAsync(
                                        flowInstanceId,
                                        barrier.NodeId,
                                        -1,
                                        evaluation.Error,
                                        now,
                                        retryAt: null,
                                        storeCancellationToken)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                await session.Nodes.MarkCompletedAsync(
                                        flowInstanceId,
                                        barrier.NodeId,
                                        -1,
                                        evaluation.Result,
                                        now,
                                        storeCancellationToken)
                                    .ConfigureAwait(false);
                            }

                            await session.Nodes.MarkDependentsReadyAsync(
                                    flowInstanceId,
                                    [barrier.NodeId],
                                    now,
                                    storeCancellationToken)
                                .ConfigureAwait(false);

                            return true;
                        }

                        return false;
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!changed)
            {
                return processed;
            }

            processed++;
        }
    }

    private BarrierEvaluation? Evaluate(
        NodeInstanceRecord barrier,
        IReadOnlyList<NodeInstanceRecord> inputs)
    {
        var outcomes = inputs
            .Select(input => new NodeOutcome(input.NodeId, input.Status))
            .ToArray();

        switch (barrier.DependencyMode)
        {
            case DependencySatisfactionMode.AllTerminal:
                return inputs.All(input => IsTerminal(input.Status))
                    ? Complete(barrier, outcomes)
                    : null;
            case DependencySatisfactionMode.AnyTerminal:
                var terminalIndex = Array.FindIndex(inputs.ToArray(), input => IsTerminal(input.Status));
                return terminalIndex >= 0 ? Complete(barrier, [outcomes[terminalIndex]]) : null;
            case DependencySatisfactionMode.AllSucceeded:
                if (inputs.Any(input => IsTerminal(input.Status) && input.Status != NodeStatus.Completed))
                {
                    return Fail(barrier, "An input node reached a non-success terminal state.");
                }

                return inputs.All(input => input.Status == NodeStatus.Completed)
                    ? Complete(barrier, outcomes)
                    : null;
            case DependencySatisfactionMode.AnySucceeded:
                var completedIndex = Array.FindIndex(inputs.ToArray(), input => input.Status == NodeStatus.Completed);
                if (completedIndex >= 0)
                {
                    return Complete(barrier, [outcomes[completedIndex]]);
                }

                return inputs.All(input => IsTerminal(input.Status))
                    ? Fail(barrier, "No input node completed successfully.")
                    : null;
            default:
                throw new ArgumentOutOfRangeException(nameof(barrier.DependencyMode));
        }
    }

    private BarrierEvaluation Complete(NodeInstanceRecord barrier, IReadOnlyList<NodeOutcome> outcomes)
    {
        return barrier.Kind switch
        {
            NodeKind.WaitAll => new BarrierEvaluation(
                serializer.Serialize(new WaitAllResult(outcomes)),
                Error: null),
            NodeKind.WaitAny => new BarrierEvaluation(
                serializer.Serialize(new WaitAnyResult(outcomes[0])),
                Error: null),
            _ => throw new InvalidOperationException($"Node '{barrier.NodeId}' is not a wait barrier.")
        };
    }

    private static BarrierEvaluation Fail(NodeInstanceRecord barrier, string reason)
        => new(Result: null, $"Wait barrier '{barrier.NodeId}' failed: {reason}");

    private static bool IsPendingBarrier(NodeInstanceRecord node)
        => node.Kind is NodeKind.WaitAll or NodeKind.WaitAny && !IsTerminal(node.Status);

    private static bool IsTerminal(NodeStatus status)
        => status is NodeStatus.Completed
            or NodeStatus.Failed
            or NodeStatus.Cancelled
            or NodeStatus.TimedOut
            or NodeStatus.Skipped;

}

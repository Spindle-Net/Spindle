using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence;

namespace Spindle;

internal sealed class StepScheduler(TimeProvider timeProvider)
{

    /// <summary>
    /// Refreshes the dependency graph and marks all steps where the dependencies are completed as ready to run.
    /// </summary>
    /// <param name="flowInstanceId">The flow instance</param>
    /// <param name="cancellationToken">The cancellation token to cancel the store requests</param>
    public async ValueTask MarkDependentsReadyAsync(
        ISpindleStoreSession storeSession,
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        var steps = await storeSession.Steps
            .GetByFlowInstanceAsync(flowInstanceId, cancellationToken)
            .ConfigureAwait(false);

        var completed = steps
            .Where(step => step.Status == StepStatus.Completed)
            .Select(step => step.StepId)
            .ToHashSet();

        foreach (var step in steps)
        {
            if (step.Status is not (StepStatus.Pending or StepStatus.Waiting))
            {
                continue;
            }

            if (step.Dependencies.Count == 0 ||
                step.Dependencies.All(completed.Contains))
            {
                await storeSession.Steps
                    .MarkReadyAsync(flowInstanceId, step.StepId, timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}

using Spindle.Abstractions.Core;

namespace Spindle.Persistence.Leases;

public interface ILeaseStore
{
    ValueTask<bool> TryAcquireStepLeaseAsync(
        StepLeaseRecord lease,
        CancellationToken cancellationToken = default);

    ValueTask ReleaseStepLeaseAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        string owner,
        CancellationToken cancellationToken = default);
}

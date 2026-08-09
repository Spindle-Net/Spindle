using Spindle.Abstractions.Core;

namespace Spindle.Persistence.Leases;

public interface ILeaseStore
{
    ValueTask<bool> TryAcquireStepLeaseAsync(
        StepLeaseRecord lease,
        CancellationToken cancellationToken = default);

    ValueTask ReleaseStepLeaseAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        string owner,
        CancellationToken cancellationToken = default);
}

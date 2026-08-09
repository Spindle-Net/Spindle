using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Persistence.EFCore.Entities;
using Spindle.Persistence.Leases;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreLeaseStore(SpindleDbContext context) : ILeaseStore
{

    public async ValueTask<bool> TryAcquireStepLeaseAsync(
        StepLeaseRecord lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();
        activity?.SetTag("spindle.efcore.lease.flow-id", lease.FlowInstanceId.Value);
        activity?.SetTag("spindle.efcore.lease.step-id", lease.NodeId.Value);
        activity?.SetTag("spindle.efcore.lease.owner", lease.Owner);

        var updated = await context.StepLeases
            .Where(existing =>
                existing.FlowInstanceId == lease.FlowInstanceId.Value &&
                existing.NodeId == lease.NodeId.Value &&
                (existing.Owner == lease.Owner || existing.ExpiresAt <= lease.AcquiredAt))
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(existing => existing.Owner, lease.Owner)
                    .SetProperty(existing => existing.AcquiredAt, lease.AcquiredAt)
                    .SetProperty(existing => existing.ExpiresAt, lease.ExpiresAt),
                cancellationToken);

        if (updated > 0)
        {
            return true;
        }

        if (await context.StepLeases.AnyAsync(
                existing =>
                    existing.FlowInstanceId == lease.FlowInstanceId.Value &&
                    existing.NodeId == lease.NodeId.Value,
                cancellationToken))
        {
            return false;
        }

        await context.StepLeases.AddAsync(new StepLeaseEntity
        {
            FlowInstanceId = lease.FlowInstanceId.Value,
            NodeId = lease.NodeId.Value,
            Owner = lease.Owner,
            AcquiredAt = lease.AcquiredAt,
            ExpiresAt = lease.ExpiresAt,
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async ValueTask ReleaseStepLeaseAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        string owner,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();
        activity?.SetTag("spindle.efcore.lease.flow-id", flowInstanceId.Value);
        activity?.SetTag("spindle.efcore.lease.step-id", nodeId.Value);
        activity?.SetTag("spindle.efcore.lease.owner", owner);

        await context.StepLeases.
            Where(x => x.FlowInstanceId == flowInstanceId.Value &&
                       x.NodeId == nodeId.Value &&
                       x.Owner == owner
            ).ExecuteDeleteAsync(cancellationToken: cancellationToken);
    }
}

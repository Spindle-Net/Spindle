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

        var updated = await context.StepLeases
            .Where(existing =>
                existing.FlowInstanceId == lease.FlowInstanceId.Value &&
                existing.StepId == lease.StepId.Value &&
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
                    existing.StepId == lease.StepId.Value,
                cancellationToken))
        {
            return false;
        }

        await context.StepLeases.AddAsync(new StepLeaseEntity
        {
            FlowInstanceId = lease.FlowInstanceId.Value,
            StepId = lease.StepId.Value,
            Owner = lease.Owner,
            AcquiredAt = lease.AcquiredAt,
            ExpiresAt = lease.ExpiresAt,
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async ValueTask ReleaseStepLeaseAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        string owner,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await context.StepLeases.
            Where(x => x.FlowInstanceId == flowInstanceId.Value &&
                       x.StepId == stepId.Value &&
                       x.Owner == owner
            ).ExecuteDeleteAsync(cancellationToken: cancellationToken);
    }
}

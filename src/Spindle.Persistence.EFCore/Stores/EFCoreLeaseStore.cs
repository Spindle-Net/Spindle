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

        var existing = await context.StepLeases.
            FirstOrDefaultAsync(x => x.FlowInstanceId == lease.FlowInstanceId.Value &&
                                     x.StepId == lease.StepId.Value, cancellationToken: cancellationToken);

        if (existing != null &&
            existing.ExpiresAt > lease.AcquiredAt &&
            !string.Equals(existing.Owner, lease.Owner, StringComparison.Ordinal))
        {
            return false; // Already allotted to another owner
        }

        // No lease, create it
        if (existing == null)
        {
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

        // Existing found, update the lifetime and owner
        existing.Owner = lease.Owner;
        existing.AcquiredAt = lease.AcquiredAt;
        existing.ExpiresAt = lease.ExpiresAt;
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

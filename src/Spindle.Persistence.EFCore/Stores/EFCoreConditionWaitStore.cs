using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Persistence.Conditions;
using Spindle.Persistence.EFCore.Entities;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreConditionWaitStore(SpindleDbContext context) : IConditionWaitStore
{
    public async ValueTask CreateAsync(
        ConditionWaitRecord wait,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await context.ConditionWaits.AnyAsync(
                existing => existing.FlowInstanceId == wait.FlowInstanceId.Value &&
                    existing.NodeId == wait.NodeId.Value,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Condition wait '{wait.NodeId}' already exists for flow instance '{wait.FlowInstanceId}'.");
        }

        await context.ConditionWaits.AddAsync(
            new ConditionWaitEntity
            {
                FlowInstanceId = wait.FlowInstanceId.Value,
                NodeId = wait.NodeId.Value,
                PollingIntervalTicks = wait.PollingInterval.Ticks,
                ExpiresAt = wait.ExpiresAt,
                CreatedAt = wait.CreatedAt
            },
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<ConditionWaitRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await context.ConditionWaits
            .AsNoTracking()
            .FirstOrDefaultAsync(
                wait => wait.FlowInstanceId == flowInstanceId.Value &&
                    wait.NodeId == nodeId.Value,
                cancellationToken);

        return entity is null
            ? null
            : new ConditionWaitRecord
            {
                FlowInstanceId = new FlowInstanceId(entity.FlowInstanceId),
                NodeId = new NodeId(entity.NodeId),
                PollingInterval = TimeSpan.FromTicks(entity.PollingIntervalTicks),
                ExpiresAt = entity.ExpiresAt,
                CreatedAt = entity.CreatedAt
            };
    }
}

using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Persistence.History;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreExecutionHistoryStore(SpindleDbContext context) : IExecutionHistoryStore
{

    public async ValueTask AppendAsync(
        ExecutionHistoryRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        await context.ExecutionHistories.AddAsync(new Entities.ExecutionHistoryEntity
        {
            FlowInstanceId = record.FlowInstanceId.Value,
            StepId = record.StepId?.Value,
            EventType = record.EventType,
            Payload = record.Payload,
            CreatedAt = record.CreatedAt,
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ExecutionHistoryRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        return await context.ExecutionHistories
            .AsNoTracking()
            .Where(x => x.FlowInstanceId == flowInstanceId.Value)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new ExecutionHistoryRecord
            {
                FlowInstanceId = new FlowInstanceId(x.FlowInstanceId),
                StepId = x.StepId == null ? null : new StepId(x.StepId),
                EventType = x.EventType,
                Payload = x.Payload,
                CreatedAt = x.CreatedAt,
            })
            .ToListAsync(cancellationToken: cancellationToken);
    }
}

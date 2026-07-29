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

        await context.ExecutionHistories.AddAsync(new Entities.ExecutionHistoryEntity
        {
            FlowInstanceId = record.FlowInstanceId.Value,
            StepId = record.StepId,
            EventType = record.EventType,
            Payload = record.Payload,
            CreatedAt = record.CreatedAt,
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ExecutionHistoryRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await context.ExecutionHistories
            .AsNoTracking()
            .Where(x => x.FlowInstanceId == flowInstanceId.Value)
            .Select(x => new ExecutionHistoryRecord
            {
                FlowInstanceId = new FlowInstanceId(x.FlowInstanceId),
                StepId = x.StepId,
                EventType = x.EventType,
                Payload = x.Payload,
                CreatedAt = x.CreatedAt,
            })
            .ToListAsync(cancellationToken: cancellationToken);
    }
}

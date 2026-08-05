using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Persistence.EFCore.Entities;
using Spindle.Persistence.Timers;
using System.Linq.Expressions;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreTimerStore(SpindleDbContext context) : ITimerStore
{

    public async ValueTask CreateAsync(
        TimerRecord timer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var existing = await context.Timers.FirstOrDefaultAsync(x => x.FlowInstanceId == timer.FlowInstanceId.Value &&
                                                                     x.StepId == timer.StepId.Value, cancellationToken);

        if (existing != null)
        {
            existing.DueAt = timer.DueAt;
            existing.FiredAt = timer.FiredAt;
        }
        else
        {
            await context.Timers.AddAsync(new Entities.TimerEntity
            {
                FlowInstanceId = timer.FlowInstanceId.Value,
                StepId = timer.StepId.Value,
                DueAt = timer.DueAt,
                CreatedAt = timer.CreatedAt,
                FiredAt = timer.FiredAt,
            }, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private readonly static Expression<Func<TimerEntity, TimerRecord>> Translation = x => new TimerRecord
    {
        FlowInstanceId = new FlowInstanceId(x.FlowInstanceId),
        StepId = new StepId(x.StepId),
        DueAt = x.DueAt,
        CreatedAt = x.CreatedAt,
        FiredAt = x.FiredAt
    };

    public async ValueTask<TimerRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        return await context.Timers
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .Select(Translation)
            .FirstOrDefaultAsync(cancellationToken);

    }

    public async ValueTask<IReadOnlyList<TimerRecord>> GetDueAsync(
        DateTimeOffset dueAtOrBefore,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        return await context.Timers
            .Where(timer => timer.FiredAt == null && timer.DueAt <= dueAtOrBefore)
            .OrderBy(timer => timer.DueAt)
            .Take(maxCount)
            .Select(Translation)
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask MarkFiredAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset firedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        await context.Timers
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.FiredAt, _ => firedAt)
            , cancellationToken);
    }
}

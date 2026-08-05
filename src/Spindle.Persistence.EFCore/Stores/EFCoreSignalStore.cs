using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Persistence.EFCore.Entities;
using Spindle.Persistence.Signals;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreSignalStore(SpindleDbContext context) : ISignalStore
{

    public async ValueTask CreateWaitAsync(
        SignalWaitRecord wait,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        await context.SignalWaits.AddAsync(new SignalWaitEntity
        {
            FlowInstanceId = wait.FlowInstanceId.Value,
            StepId = wait.StepId.Value,
            SignalName = wait.SignalName.Value,
            CorrelationKey = wait.CorrelationKey?.Value,
            CreatedAt = wait.CreatedAt,
            ExpiresAt = wait.ExpiresAt,
            CompletedAt = wait.CompletedAt,
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<SignalWaitRecord>> GetOpenWaitsAsync(
        SignalName signalName,
        CorrelationKey? correlationKey = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        return await context.SignalWaits
            .Where(wait =>
                wait.CompletedAt == null &&
                wait.SignalName == signalName.Value &&
                (correlationKey == null || wait.CorrelationKey == correlationKey.Value.Value))
            .OrderBy(wait => wait.CreatedAt)
            .Select(x => new SignalWaitRecord
            {
                FlowInstanceId = new FlowInstanceId(x.FlowInstanceId),
                StepId = new StepId(x.StepId),
                SignalName = new SignalName(x.SignalName),
                CorrelationKey = x.CorrelationKey != null ? new CorrelationKey(x.CorrelationKey) : null,
                CreatedAt = x.CreatedAt,
                ExpiresAt = x.ExpiresAt,
                CompletedAt = x.CompletedAt
            })
            .ToArrayAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask MarkWaitCompletedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        await context.SignalWaits
            .Where(x => x.FlowInstanceId == flowInstanceId.Value &&
                        x.StepId == stepId.Value)
            .ExecuteUpdateAsync(
                x => x.SetProperty(y => y.CompletedAt, _ => completedAt),
                cancellationToken: cancellationToken);
    }

    public async ValueTask AppendSignalAsync(
        SignalRecord signal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        await context.Signals.AddAsync(new EFCore.Entities.SignalEntity
        {
            SignalName = signal.SignalName.Value,
            CorrelationKey = signal.CorrelationKey?.Value,
            FlowInstanceId = signal.FlowInstanceId?.Value,
            Payload = signal.Payload,
            RaisedAt = signal.RaisedAt,
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SignalRecord>> GetSignals()
    {
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        return await context.Signals
            .AsNoTracking()
            .Select(x => new SignalRecord
            {
                SignalName = new SignalName(x.SignalName),
                CorrelationKey = x.CorrelationKey != null ? new CorrelationKey(x.CorrelationKey) : null,
                FlowInstanceId = x.FlowInstanceId != null ? new FlowInstanceId(x.FlowInstanceId) : null,
                Payload = x.Payload,
                RaisedAt = x.RaisedAt
            }).ToArrayAsync();
    }
}

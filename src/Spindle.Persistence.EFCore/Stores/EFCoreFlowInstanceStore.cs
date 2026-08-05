using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.EFCore.Entities;
using Spindle.Persistence.FlowInstances;
using System.Linq.Expressions;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreFlowInstanceStore(SpindleDbContext context) : IFlowInstanceStore
{
    public async ValueTask CreateAsync(
        FlowInstanceRecord instance,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var existing = await context.FlowInstances.FirstOrDefaultAsync(x =>
            (x.InstanceId == instance.InstanceId.Value) || // Either the instance itself exists already OR
            (instance.IdempotencyKey != null && x.FlowName == instance.FlowName.Value &&
                x.IdempotencyKey == instance.IdempotencyKey),
            cancellationToken);

        if (existing != null)
        {
            if (existing.InstanceId == instance.InstanceId.Value)
                throw new InvalidOperationException(
                    $"Flow instance '{instance.InstanceId}' already exists.");

            if (instance.IdempotencyKey is { } idempotencyKey && existing.IdempotencyKey == idempotencyKey)
            {
                throw new InvalidOperationException(
                    $"Flow '{instance.FlowName}' already has an instance for idempotency key '{idempotencyKey}'.");
            }
        }

        await context.FlowInstances.AddAsync(new FlowInstanceEntity
        {
            InstanceId = instance.InstanceId.Value,
            FlowName = instance.FlowName.Value,
            FlowVersion = instance.FlowVersion.Value,
            DefinitionHash = instance.DefinitionHash,
            Status = instance.Status,
            Input = instance.Input,
            Result = instance.Result,
            Error = instance.Error,
            CorrelationKey = instance.CorrelationKey?.Value,
            IdempotencyKey = instance.IdempotencyKey,
            CreatedAt = instance.CreatedAt,
            CompletedAt = instance.CompletedAt,
            UpdatedAt = instance.UpdatedAt,
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private readonly static Expression<Func<FlowInstanceEntity, FlowInstanceRecord>> Transformer = x => new FlowInstanceRecord
    {
        InstanceId = new FlowInstanceId(x.InstanceId),
        FlowName = new FlowName(x.FlowName),
        FlowVersion = new FlowVersion(x.FlowVersion),
        DefinitionHash = x.DefinitionHash,
        Status = x.Status,
        Input = x.Input,
        Result = x.Result,
        Error = x.Error,
        CorrelationKey = x.CorrelationKey != null ? new CorrelationKey(x.CorrelationKey) : null,
        IdempotencyKey = x.IdempotencyKey,
        CreatedAt = x.CreatedAt,
        CompletedAt = x.CompletedAt,
        UpdatedAt = x.UpdatedAt
    };

    public async ValueTask<FlowInstanceRecord?> GetAsync(
        FlowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        return await context.FlowInstances
            .AsNoTracking()
            .Where(x => x.InstanceId == instanceId.Value)
            .Select(Transformer)
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask<FlowInstanceRecord?> GetByIdempotencyKeyAsync(
        FlowName flowName,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        return await context.FlowInstances
            .AsNoTracking()
            .Where(existing =>
                existing.FlowName == flowName.Value &&
                existing.IdempotencyKey == idempotencyKey)
            .Select(Transformer)
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask<IReadOnlyList<FlowInstanceRecord>> GetRunnableAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        return await context.FlowInstances
            .AsNoTracking()
            .Where(instance =>
                instance.Status != FlowInstanceStatus.Completed &&
                instance.Status != FlowInstanceStatus.Failed &&
                instance.Status != FlowInstanceStatus.Cancelled &&
                instance.Status != FlowInstanceStatus.TimedOut)
            .OrderBy(instance => instance.UpdatedAt)
            .ThenBy(instance => instance.CreatedAt)
            .ThenBy(instance => instance.InstanceId)
            .Take(maxCount)
            .Select(Transformer)
            .ToArrayAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask UpdateStatusAsync(
        FlowInstanceId instanceId,
        FlowInstanceStatus status,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        await context.FlowInstances
            .Where(x => x.InstanceId == instanceId.Value)
            .ExecuteUpdateAsync(x =>
                x
                    .SetProperty(y => y.Status, _ => status)
                    .SetProperty(x => x.UpdatedAt, _ => updatedAt)
                , cancellationToken: cancellationToken);
    }

    public async ValueTask MarkCompletedAsync(
        FlowInstanceId instanceId,
        SerializedPayload result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var instance = await context.FlowInstances
            .FirstOrDefaultAsync(
                x => x.InstanceId == instanceId.Value,
                cancellationToken);

        if (instance == null)
        {
            return;
        }

        instance.Status = FlowInstanceStatus.Completed;
        instance.Result = result;
        instance.Error = null;
        instance.CompletedAt = completedAt;
        instance.UpdatedAt = completedAt;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask MarkFailedAsync(
        FlowInstanceId instanceId,
        string error,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        await context.FlowInstances
            .Where(x => x.InstanceId == instanceId.Value)
            .ExecuteUpdateAsync(x =>
                x
                    .SetProperty(y => y.Status, _ => FlowInstanceStatus.Failed)
                    .SetProperty(x => x.Error, _ => error)
                    .SetProperty(x => x.CompletedAt, _ => failedAt)
                    .SetProperty(x => x.UpdatedAt, _ => failedAt)
                , cancellationToken: cancellationToken);
    }

}

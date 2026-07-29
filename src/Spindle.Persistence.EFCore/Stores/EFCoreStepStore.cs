using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.EFCore.Entities;
using Spindle.Persistence.Steps;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreStepStore(SpindleDbContext context) : IStepStore
{

    private StepInstanceEntity RecordToEntity(StepInstanceRecord step) => new()
    {
        FlowInstanceId = step.FlowInstanceId.Value,
        StepId = step.StepId.Value,
        Name = step.Name,
        Kind = step.Kind,
        Status = step.Status,
        HandlerId = step.HandlerId?.Value,
        Queue = step.Queue?.Value,
        DispatchMode = step.DispatchMode,
        Dependencies = step.Dependencies.Select(d => d.Value).ToList(),
        Input = step.Input,
        Result = step.Result,
        Error = step.Error,
        Attempt = step.Attempt,
        RetryAt = step.RetryAt,
        StartedAt = step.StartedAt,
        CompletedAt = step.CompletedAt,
        CreatedAt = step.CreatedAt,
        UpdatedAt = step.UpdatedAt,
    };

    public async ValueTask CreateAsync(
        StepInstanceRecord step,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var alreadyExists = await context.StepInstances.AnyAsync(x => x.FlowInstanceId == step.FlowInstanceId.Value &&
                                                                      x.StepId == step.StepId.Value);
        if (alreadyExists)
        {
            throw new InvalidOperationException(
                    $"Step '{step.StepId}' already exists for flow instance '{step.FlowInstanceId}'.");
        }

        await context.StepInstances.AddAsync(RecordToEntity(step), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask CreateManyAsync(
        IReadOnlyList<StepInstanceRecord> steps,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(steps);

        var groupedSteps = steps.GroupBy(x => x.FlowInstanceId).Select(x => new
        {
            FlowInstanceId = x.Key.Value,
            StepIds = x.Select(y => y.StepId.Value).ToList()
        });

        var existing = new List<StepInstanceEntity>();
        foreach (var group in groupedSteps)
        {
            existing.AddRange(await context.StepInstances
                .Where(x => x.FlowInstanceId == group.FlowInstanceId && group.StepIds.Contains(x.StepId))
                .ToListAsync(cancellationToken));
        }

        if (existing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{existing.Count} steps already exists: {string.Join(", ", existing.Select(x => $"{{Flow={x.FlowInstanceId}, Step={x.StepId}}}"))}");
        }

        var keys = new HashSet<(FlowInstanceId FlowInstanceId, StepId StepId)>();
        foreach (var step in steps)
        {
            var key = (step.FlowInstanceId, step.StepId);

            if (!keys.Add(key))
            {
                throw new InvalidOperationException(
                    $"Attempted to add duplicate step '{step.StepId}' for flow instance '{step.FlowInstanceId}'.");
            }
        }


        await context.StepInstances.AddRangeAsync(steps.Select(RecordToEntity), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static StepInstanceRecord EntityToRecord(StepInstanceEntity x) => new()
    {
        FlowInstanceId = new FlowInstanceId(x.FlowInstanceId),
        StepId = new StepId(x.StepId),
        Name = x.Name,
        Kind = x.Kind,
        Status = x.Status,
        HandlerId = x.HandlerId != null ? new StepHandlerId(x.HandlerId) : null,
        Queue = x.Queue != null ? new QueueName(x.Queue) : null,
        DispatchMode = x.DispatchMode,
        Dependencies = x.Dependencies.Select(y => new StepId(y)).ToList(),
        Input = x.Input,
        Result = x.Result,
        Error = x.Error,
        Attempt = x.Attempt,
        RetryAt = x.RetryAt,
        CompletedAt = x.CompletedAt,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt,
    };

    public async ValueTask<StepInstanceRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await context.StepInstances
            .AsNoTracking()
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : EntityToRecord(entity);
    }

    public async ValueTask<IReadOnlyList<StepInstanceRecord>> GetManyAsync(
        FlowInstanceId flowInstanceId,
        IReadOnlyList<StepId> stepIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(stepIds);

        var ids = stepIds.Select(x => x.Value).Distinct().ToList();

        var entities = await context.StepInstances
            .AsNoTracking()
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && ids.Contains(x.StepId))
            .ToListAsync(cancellationToken);

        return entities.Select(EntityToRecord).ToArray();
    }

    public async ValueTask<IReadOnlyList<StepInstanceRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entities = await context.StepInstances
            .AsNoTracking()
            .Where(x => x.FlowInstanceId == flowInstanceId.Value)
            .OrderBy(step => step.CreatedAt)
            .ThenBy(x => x.StepId)
            .ToListAsync(cancellationToken);

        return entities.Select(EntityToRecord).ToArray();
    }

    public async ValueTask<IReadOnlyList<StepInstanceRecord>> GetReadyStepsAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entities = await context.StepInstances
            .AsNoTracking()
            .Where(x => x.Status == StepStatus.Ready)
            .OrderBy(step => step.CreatedAt)
            .ThenBy(x => x.StepId)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        return entities.Select(EntityToRecord).ToArray();
    }

    public async ValueTask MarkReadyAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var currentStatus = await context.StepInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .Select(x => (StepStatus?)x.Status).FirstOrDefaultAsync(cancellationToken);

        if (currentStatus == null) throw new InvalidOperationException(
                $"Step '{stepId}' does not exist for flow instance '{flowInstanceId}'.");

        var isTerminal = currentStatus is StepStatus.Completed or StepStatus.Failed or StepStatus.Cancelled;
        if (isTerminal) return;

        // It does exist and is not terminal, so we can set it as ready
        await context.StepInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => StepStatus.Ready)
                .SetProperty(x => x.UpdatedAt, _ => updatedAt)
            , cancellationToken);
    }

    public async ValueTask MarkRunningAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        StepAttemptId attemptId,
        string workerId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var prevAttempt = await context.StepInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .Select(x => new { x.Attempt }).FirstOrDefaultAsync(cancellationToken);

        if (prevAttempt == null) throw new InvalidOperationException(
                $"Step '{stepId}' does not exist for flow instance '{flowInstanceId}'.");

        await context.StepInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => StepStatus.Running)
                .SetProperty(x => x.Attempt, _ => prevAttempt.Attempt + 1)
                .SetProperty(x => x.StartedAt, _ => startedAt)
                .SetProperty(x => x.UpdatedAt, _ => startedAt)
            , cancellationToken);

        await context.StepAttempts.AddAsync(new StepAttemptEntity
        {
            FlowInstanceId = flowInstanceId.Value,
            StepId = stepId.Value,
            AttemptId = attemptId.Value,
            Attempt = prevAttempt.Attempt + 1,
            WorkerId = workerId,
            Status = StepStatus.Running,
            StartedAt = startedAt,
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask MarkWaitingAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exists = await context.StepInstances
            .AnyAsync(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value, cancellationToken);

        if (!exists) throw new InvalidOperationException(
                $"Step '{stepId}' does not exist for flow instance '{flowInstanceId}'.");

        await context.StepInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => StepStatus.Waiting)
                .SetProperty(x => x.UpdatedAt, _ => updatedAt)
            , cancellationToken);
    }

    public async ValueTask MarkCompletedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        SerializedPayload? result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exists = await context.StepInstances
            .AnyAsync(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value, cancellationToken);

        if (!exists) throw new InvalidOperationException(
                $"Step '{stepId}' does not exist for flow instance '{flowInstanceId}'.");

        await context.StepInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => StepStatus.Completed)
                .SetProperty(x => x.Result, _ => result)
                .SetProperty(x => x.Error, _ => null)
                .SetProperty(x => x.CompletedAt, _ => completedAt)
                .SetProperty(x => x.UpdatedAt, _ => completedAt)
            , cancellationToken);

        await CompleteLatestAttempt(flowInstanceId, stepId, StepStatus.Completed, completedAt, null);
    }

    public async ValueTask MarkFailedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exists = await context.StepInstances
            .AnyAsync(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value, cancellationToken);

        if (!exists) throw new InvalidOperationException(
                $"Step '{stepId}' does not exist for flow instance '{flowInstanceId}'.");

        await context.StepInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => StepStatus.Failed)
                .SetProperty(x => x.Error, _ => error)
                .SetProperty(x => x.RetryAt, _ => retryAt)
                .SetProperty(x => x.CompletedAt, _ => failedAt)
                .SetProperty(x => x.UpdatedAt, _ => failedAt)
            , cancellationToken);

        await CompleteLatestAttempt(flowInstanceId, stepId, StepStatus.Failed, failedAt, error);
    }

    private async Task CompleteLatestAttempt(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        StepStatus status,
        DateTimeOffset completedAt,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await context.StepAttempts
            // Find the last one
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.StepId == stepId.Value && !x.CompletedAt.HasValue)
            .OrderByDescending(x => x.Attempt)
            .Take(1)

            // Update it
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => status)
                .SetProperty(x => x.CompletedAt, _ => completedAt)
                .SetProperty(x => x.Error, _ => error)
            , cancellationToken);
    }
}

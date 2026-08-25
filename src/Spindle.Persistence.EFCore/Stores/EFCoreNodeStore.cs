using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.EFCore.Entities;
using Spindle.Persistence.Nodes;
using System.Diagnostics;

namespace Spindle.Persistence.EFCore.Stores;

internal sealed class EFCoreNodeStore(SpindleDbContext context) : INodeStore
{

    private NodeInstanceEntity RecordToEntity(NodeInstanceRecord node) => new()
    {
        FlowInstanceId = node.FlowInstanceId.Value,
        NodeId = node.NodeId.Value,
        Name = node.Name,
        Kind = node.Kind,
        Status = node.Status,
        HandlerId = node.HandlerId?.Value,
        Queue = node.Queue?.Value,
        DispatchMode = node.DispatchMode,
        DependencyMode = node.DependencyMode,
        Dependencies = node.Dependencies.Select((dependency, position) => new NodeDependencyEntity
        {
            FlowInstanceId = node.FlowInstanceId.Value,
            NodeId = node.NodeId.Value,
            DependsOnId = dependency.Value,
            Position = position,
        }).ToList(),
        Dependents = [],
        Input = node.Input,
        Result = node.Result,
        Error = node.Error,
        Attempt = node.Attempt,
        RetryAt = node.RetryAt,
        StartedAt = node.StartedAt,
        CompletedAt = node.CompletedAt,
        CreatedAt = node.CreatedAt,
        UpdatedAt = node.UpdatedAt,
    };

    public async ValueTask CreateAsync(
        NodeInstanceRecord node,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var alreadyExists = await context.NodeInstances.AnyAsync(x => x.FlowInstanceId == node.FlowInstanceId.Value &&
                                                                      x.NodeId == node.NodeId.Value, cancellationToken);
        if (alreadyExists)
        {
            throw new InvalidOperationException(
                    $"Node '{node.NodeId}' already exists for flow instance '{node.FlowInstanceId}'.");
        }

        await context.NodeInstances.AddAsync(RecordToEntity(node), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask CreateManyAsync(
        IReadOnlyList<NodeInstanceRecord> nodes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(nodes);
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var groupedNodes = nodes.GroupBy(x => x.FlowInstanceId).Select(x => new
        {
            FlowInstanceId = x.Key.Value,
            NodeIds = x.Select(y => y.NodeId.Value).ToList()
        });

        var existing = new List<NodeInstanceEntity>();
        foreach (var group in groupedNodes)
        {
            existing.AddRange(await context.NodeInstances
                .Where(x => x.FlowInstanceId == group.FlowInstanceId && group.NodeIds.Contains(x.NodeId))
                .ToListAsync(cancellationToken));
        }

        if (existing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{existing.Count} nodes already exists: {string.Join(", ", existing.Select(x => $"{{Flow={x.FlowInstanceId}, Step={x.NodeId}}}"))}");
        }

        var keys = new HashSet<(FlowInstanceId FlowInstanceId, NodeId NodeId)>();
        foreach (var node in nodes)
        {
            var key = (node.FlowInstanceId, node.NodeId);

            if (!keys.Add(key))
            {
                throw new InvalidOperationException(
                    $"Attempted to add duplicate node '{node.NodeId}' for flow instance '{node.FlowInstanceId}'.");
            }
        }


        await context.NodeInstances.AddRangeAsync(nodes.Select(RecordToEntity), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static NodeInstanceRecord EntityToRecord(NodeInstanceEntity x) => new()
    {
        FlowInstanceId = new FlowInstanceId(x.FlowInstanceId),
        NodeId = new NodeId(x.NodeId),
        Name = x.Name,
        Kind = x.Kind,
        Status = x.Status,
        HandlerId = x.HandlerId != null ? new StepHandlerId(x.HandlerId) : null,
        Queue = x.Queue != null ? new QueueName(x.Queue) : null,
        DispatchMode = x.DispatchMode,
        DependencyMode = x.DependencyMode,
        Dependencies = x.Dependencies
            .OrderBy(dependency => dependency.Position)
            .Select(dependency => new NodeId(dependency.DependsOnId))
            .ToList(),
        Input = x.Input,
        Result = x.Result,
        Error = x.Error,
        Attempt = x.Attempt,
        RetryAt = x.RetryAt,
        StartedAt = x.StartedAt,
        CompletedAt = x.CompletedAt,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt,
    };

    public async ValueTask<NodeInstanceRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var entity = await context.NodeInstances
            .AsNoTracking()
            .Include(x => x.Dependencies)
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : EntityToRecord(entity);
    }

    public async ValueTask<IReadOnlyList<NodeInstanceRecord>> GetManyAsync(
        FlowInstanceId flowInstanceId,
        IReadOnlyList<NodeId> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(nodeIds);
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var ids = nodeIds.Select(x => x.Value).Distinct().ToList();

        var entities = await context.NodeInstances
            .AsNoTracking()
            .Include(x => x.Dependencies)
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && ids.Contains(x.NodeId))
            .ToListAsync(cancellationToken);

        return entities.Select(EntityToRecord).ToArray();
    }

    public async ValueTask<IReadOnlyList<NodeInstanceRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var entities = await context.NodeInstances
            .AsNoTracking()
            .Include(x => x.Dependencies)
            .Where(x => x.FlowInstanceId == flowInstanceId.Value)
            .OrderBy(node => node.CreatedAt)
            .ThenBy(x => x.NodeId)
            .ToListAsync(cancellationToken);

        return entities.Select(EntityToRecord).ToArray();
    }

    public async ValueTask<IReadOnlyList<NodeInstanceRecord>> GetReadyNodesAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var entities = await context.NodeInstances
            .AsNoTracking()
            .Include(x => x.Dependencies)
            .Where(x => x.Status == NodeStatus.Ready)
            .OrderBy(node => node.CreatedAt)
            .ThenBy(x => x.NodeId)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        return entities.Select(EntityToRecord).ToArray();
    }

    public async ValueTask MarkReadyAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var currentStatus = await context.NodeInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value)
            .Select(x => (NodeStatus?)x.Status).FirstOrDefaultAsync(cancellationToken);

        if (currentStatus == null) throw new InvalidOperationException(
                $"Node '{nodeId}' does not exist for flow instance '{flowInstanceId}'.");

        var isTerminal = currentStatus is NodeStatus.Completed
            or NodeStatus.Failed
            or NodeStatus.Cancelled
            or NodeStatus.TimedOut
            or NodeStatus.Skipped;
        if (isTerminal) return;

        // It does exist and is not terminal, so we can set it as ready
        await context.NodeInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => NodeStatus.Ready)
                .SetProperty(x => x.UpdatedAt, _ => updatedAt)
            , cancellationToken);
    }

    public async ValueTask MarkRunningAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        StepAttemptId attemptId,
        string workerId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var prevAttempt = await context.NodeInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value)
            .Select(x => new { x.Attempt }).FirstOrDefaultAsync(cancellationToken);

        if (prevAttempt == null) throw new InvalidOperationException(
                $"Node '{nodeId}' does not exist for flow instance '{flowInstanceId}'.");

        await context.NodeInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => NodeStatus.Running)
                .SetProperty(x => x.Attempt, _ => prevAttempt.Attempt + 1)
                .SetProperty(x => x.StartedAt, _ => startedAt)
                .SetProperty(x => x.UpdatedAt, _ => startedAt)
            , cancellationToken);

        await context.StepAttempts.AddAsync(new StepAttemptEntity
        {
            FlowInstanceId = flowInstanceId.Value,
            NodeId = nodeId.Value,
            AttemptId = attemptId.Value,
            Attempt = prevAttempt.Attempt + 1,
            WorkerId = workerId,
            Status = NodeStatus.Running,
            StartedAt = startedAt,
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask MarkWaitingAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var exists = await context.NodeInstances
            .AnyAsync(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value, cancellationToken);

        if (!exists) throw new InvalidOperationException(
                $"Node '{nodeId}' does not exist for flow instance '{flowInstanceId}'.");

        await context.NodeInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => NodeStatus.Waiting)
                .SetProperty(x => x.UpdatedAt, _ => updatedAt)
            , cancellationToken);

        await CompleteAttemptAsync(
            flowInstanceId,
            nodeId,
            attempt,
            NodeStatus.Waiting,
            updatedAt,
            null,
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask MarkTimedOutAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        string error,
        DateTimeOffset timedOutAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var updated = await context.NodeInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, _ => NodeStatus.TimedOut)
                .SetProperty(x => x.Error, _ => error)
                .SetProperty(x => x.CompletedAt, _ => timedOutAt)
                .SetProperty(x => x.UpdatedAt, _ => timedOutAt),
                cancellationToken);

        if (updated == 0)
        {
            throw new InvalidOperationException(
                $"Node '{nodeId}' does not exist for flow instance '{flowInstanceId}'.");
        }

        await CompleteAttemptAsync(
            flowInstanceId,
            nodeId,
            attempt,
            NodeStatus.TimedOut,
            timedOutAt,
            error,
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask MarkCompletedAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        SerializedPayload? result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var node = await context.NodeInstances
            .FirstOrDefaultAsync(
                x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value,
                cancellationToken);

        if (node == null) throw new InvalidOperationException(
                $"Node '{nodeId}' does not exist for flow instance '{flowInstanceId}'.");

        node.Status = NodeStatus.Completed;
        node.Result = result;
        node.Error = null;
        node.CompletedAt = completedAt;
        node.UpdatedAt = completedAt;

        await CompleteAttemptAsync(flowInstanceId, nodeId, attempt, NodeStatus.Completed, completedAt, null, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask MarkFailedAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        var exists = await context.NodeInstances
            .AnyAsync(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value, cancellationToken);

        if (!exists) throw new InvalidOperationException(
                $"Node '{nodeId}' does not exist for flow instance '{flowInstanceId}'.");

        await context.NodeInstances
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => NodeStatus.Failed)
                .SetProperty(x => x.Error, _ => error)
                .SetProperty(x => x.RetryAt, _ => retryAt)
                .SetProperty(x => x.CompletedAt, _ => failedAt)
                .SetProperty(x => x.UpdatedAt, _ => failedAt)
            , cancellationToken);

        await CompleteAttemptAsync(flowInstanceId, nodeId, attempt, NodeStatus.Failed, failedAt, error, cancellationToken);
    }

    public async ValueTask MarkDependentsReadyAsync(
        FlowInstanceId flowInstanceId,
        List<NodeId>? updatedNodes,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();
        activity?.SetTag("spindle.efcore.updated-nodes", updatedNodes?.Count ?? 0);

        var q = context.NodeInstances
                .Where(x => x.FlowInstanceId == flowInstanceId.Value)
                .Where(x =>
                    (x.Kind == NodeKind.Step || x.Kind == NodeKind.ConditionWait) &&
                    x.Status == NodeStatus.Pending);

        if (updatedNodes is { Count: > 0 })
        {
            var updatedNodeIds = updatedNodes.Select(x => x.Value).ToList();

            // Keep NodeInstances as the update root. ExecuteUpdateAsync cannot update a
            // query whose target is reached through SelectMany over a navigation property.

            if (updatedNodeIds.Count == 1)
            {
                // Single item -> Skip the JSON parameter serialization
                var id = updatedNodeIds.First();
                q = q.Where(x => x.Dependencies.Any(y => y.DependsOnId == id));
            }
            else
            {
                // Multiple items -> Use IN (OPENJSON)
                q = q.Where(x => x.Dependencies.Any(y => updatedNodeIds.Contains(y.DependsOnId)));
            }
        }

        await q.Where(x => x.Dependencies.All(y => y.DependsOn!.Status == NodeStatus.Completed))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.Status, _ => NodeStatus.Ready)
                    .SetProperty(x => x.UpdatedAt, _ => updatedAt)
                , cancellationToken);
    }

    private async Task CompleteAttemptAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        NodeStatus status,
        DateTimeOffset completedAt,
        string? error,
        CancellationToken cancellationToken = default)
    {
        using var activity = SpindleEFCoreTelemetry.ActivitySource.StartActivity();

        await context.StepAttempts
            // Find the last one
            .Where(x => x.FlowInstanceId == flowInstanceId.Value && x.NodeId == nodeId.Value && !x.CompletedAt.HasValue && x.Attempt == attempt)

            // Update it
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, _ => status)
                .SetProperty(x => x.CompletedAt, _ => completedAt)
                .SetProperty(x => x.Error, _ => error)
            , cancellationToken);
    }
}

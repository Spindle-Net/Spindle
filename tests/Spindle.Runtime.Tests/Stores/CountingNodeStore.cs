using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.Nodes;

namespace Spindle.Runtime.Tests.Stores;

internal sealed class CountingNodeStore(
        INodeStore inner)
        : INodeStore
{
    public int CreateCalls { get; private set; }

    public int CreateManyCalls { get; private set; }

    public int CreatedInBatches { get; private set; }

    public int GetAsyncCalls { get; private set; }

    public int GetManyCalls { get; private set; }

    public int GetByFlowInstanceCalls { get; private set; }

    public int GetReadyNodesCalls { get; private set; }

    public int MarkReadyCalls { get; private set; }

    public int MarkRunningCalls { get; private set; }

    public int MarkWaitingCalls { get; private set; }

    public int MarkCompletedCalls { get; private set; }

    public int MarkFailedCalls { get; private set; }

    public int MarkDependentsReadyCalls { get; private set; }

    public void Reset()
    {
        CreateCalls = 0;
        CreateManyCalls = 0;
        CreatedInBatches = 0;
        GetAsyncCalls = 0;
        GetManyCalls = 0;
        GetByFlowInstanceCalls = 0;
        GetReadyNodesCalls = 0;
        MarkReadyCalls = 0;
        MarkRunningCalls = 0;
        MarkWaitingCalls = 0;
        MarkCompletedCalls = 0;
        MarkFailedCalls = 0;
        MarkDependentsReadyCalls = 0;
    }

    public ValueTask CreateAsync(
        NodeInstanceRecord step,
        CancellationToken cancellationToken = default)
    {
        CreateCalls++;
        return inner.CreateAsync(step, cancellationToken);
    }

    public ValueTask CreateManyAsync(
        IReadOnlyList<NodeInstanceRecord> steps,
        CancellationToken cancellationToken = default)
    {
        CreateManyCalls++;
        CreatedInBatches += steps.Count;
        return inner.CreateManyAsync(steps, cancellationToken);
    }

    public ValueTask<NodeInstanceRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        CancellationToken cancellationToken = default)
    {
        GetAsyncCalls++;
        return inner.GetAsync(flowInstanceId, nodeId, cancellationToken);
    }

    public ValueTask<IReadOnlyList<NodeInstanceRecord>> GetManyAsync(
        FlowInstanceId flowInstanceId,
        IReadOnlyList<NodeId> nodeIds,
        CancellationToken cancellationToken = default)
    {
        GetManyCalls++;
        return inner.GetManyAsync(flowInstanceId, nodeIds, cancellationToken);
    }

    public ValueTask<IReadOnlyList<NodeInstanceRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        GetByFlowInstanceCalls++;
        return inner.GetByFlowInstanceAsync(flowInstanceId, cancellationToken);
    }

    public ValueTask<IReadOnlyList<NodeInstanceRecord>> GetReadyNodesAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        GetReadyNodesCalls++;
        return inner.GetReadyNodesAsync(maxCount, cancellationToken);
    }

    public ValueTask MarkReadyAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        MarkReadyCalls++;
        return inner.MarkReadyAsync(flowInstanceId, nodeId, updatedAt, cancellationToken);
    }

    public ValueTask MarkRunningAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        StepAttemptId attemptId,
        string workerId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        MarkRunningCalls++;
        return inner.MarkRunningAsync(flowInstanceId, nodeId, attemptId, workerId, startedAt, cancellationToken);
    }

    public ValueTask MarkWaitingAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        MarkWaitingCalls++;
        return inner.MarkWaitingAsync(flowInstanceId, nodeId, attempt, updatedAt, cancellationToken);
    }

    public ValueTask MarkTimedOutAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        string error,
        DateTimeOffset timedOutAt,
        CancellationToken cancellationToken = default)
        => inner.MarkTimedOutAsync(flowInstanceId, nodeId, attempt, error, timedOutAt, cancellationToken);

    public ValueTask MarkCompletedAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        SerializedPayload? result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        MarkCompletedCalls++;
        return inner.MarkCompletedAsync(flowInstanceId, nodeId, attempt, result, completedAt, cancellationToken);
    }

    public ValueTask MarkFailedAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default)
    {
        MarkFailedCalls++;
        return inner.MarkFailedAsync(flowInstanceId, nodeId, attempt, error, failedAt, retryAt, cancellationToken);
    }

    public ValueTask MarkDependentsReadyAsync(
        FlowInstanceId flowInstanceId,
        List<NodeId>? updatedNodes,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        MarkDependentsReadyCalls++;
        return inner.MarkDependentsReadyAsync(flowInstanceId, updatedNodes, updatedAt, cancellationToken);
    }

}

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

using Spindle.Persistence.Nodes;

namespace Spindle.Persistence.Nodes;

public interface INodeStore
{
    ValueTask CreateAsync(
        NodeInstanceRecord node,
        CancellationToken cancellationToken = default);

    ValueTask CreateManyAsync(
        IReadOnlyList<NodeInstanceRecord> nodes,
        CancellationToken cancellationToken = default);

    ValueTask<NodeInstanceRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NodeInstanceRecord>> GetManyAsync(
        FlowInstanceId flowInstanceId,
        IReadOnlyList<NodeId> nodeIds,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NodeInstanceRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NodeInstanceRecord>> GetReadyNodesAsync(
        int maxCount,
        CancellationToken cancellationToken = default);

    ValueTask MarkReadyAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkRunningAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        StepAttemptId attemptId,
        string workerId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkWaitingAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkCompletedAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        SerializedPayload? result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkFailedAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks dependent nodes as ready once its dependencies are completed.
    /// </summary>
    /// <param name="flowInstanceId">The flow instance to process.</param>
    /// <param name="updatedNodes">
    /// Optional list of node IDs that were updated; when provided, the store may limit processing to dependents of these nodes.
    /// </param>
    /// <param name="updatedAt">Timestamp to record on updated node instances.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when dependent nodes have been marked as ready.</returns>
    ValueTask MarkDependentsReadyAsync(
        FlowInstanceId flowInstanceId,
        List<NodeId>? updatedNodes,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}

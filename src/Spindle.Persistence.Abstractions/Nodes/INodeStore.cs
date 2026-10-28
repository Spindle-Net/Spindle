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

    /// <summary>
    /// Gets every recorded attempt of a node, ordered by attempt number.
    /// </summary>
    ValueTask<IReadOnlyList<StepAttemptRecord>> GetAttemptsAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
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
        int attempt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkTimedOutAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        string error,
        DateTimeOffset timedOutAt,
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
    /// Closes a failed attempt and puts the node in <see cref="NodeStatus.Waiting"/> until <paramref name="retryAt"/>.
    /// </summary>
    /// <param name="attempt">The attempt that failed. The update only applies while the node is running this attempt.</param>
    /// <param name="attemptOutcome">How the attempt ended, usually <see cref="NodeStatus.Failed"/> or <see cref="NodeStatus.TimedOut"/>.</param>
    /// <returns><c>true</c> if the node was updated.</returns>
    ValueTask<bool> TryScheduleRetryAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        NodeStatus attemptOutcome,
        string error,
        DateTimeOffset retryAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a node waiting for the retry due at <paramref name="retryAt"/> to <see cref="NodeStatus.Ready"/>
    /// and clears its retry time.
    /// </summary>
    /// <returns><c>true</c> if the node was updated; <c>false</c> for a stale or unknown retry.</returns>
    ValueTask<bool> TryMarkRetryReadyAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset retryAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes an attempt that was interrupted by host cancellation as <see cref="NodeStatus.Cancelled"/> and
    /// returns the node to <see cref="NodeStatus.Ready"/>. The interrupted attempt does not count towards the
    /// node's retry budget.
    /// </summary>
    /// <returns><c>true</c> if the node was still running <paramref name="attempt"/> and was updated.</returns>
    ValueTask<bool> TryInterruptAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        DateTimeOffset interruptedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a manual retry of a failed or timed-out node at <paramref name="retryAt"/> and starts a new
    /// retry budget from the node's current attempt.
    /// </summary>
    /// <returns><c>true</c> if the node still had <paramref name="expectedStatus"/> and <paramref name="expectedAttempt"/>.</returns>
    ValueTask<bool> TryScheduleRecoveryAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        NodeStatus expectedStatus,
        int expectedAttempt,
        DateTimeOffset retryAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a failed wait barrier to <see cref="NodeStatus.Waiting"/> so it is evaluated again.
    /// </summary>
    /// <returns><c>true</c> if the node was a failed barrier and was updated.</returns>
    ValueTask<bool> TryReopenBarrierAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset updatedAt,
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

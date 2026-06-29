using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.Steps;

public interface IStepStore
{
    ValueTask CreateAsync(
        StepInstanceRecord step,
        CancellationToken cancellationToken = default);

    ValueTask CreateManyAsync(
        IReadOnlyList<StepInstanceRecord> steps,
        CancellationToken cancellationToken = default);

    ValueTask<StepInstanceRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StepInstanceRecord>> GetManyAsync(
        FlowInstanceId flowInstanceId,
        IReadOnlyList<StepId> stepIds,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StepInstanceRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StepInstanceRecord>> GetReadyStepsAsync(
        int maxCount,
        CancellationToken cancellationToken = default);

    ValueTask MarkReadyAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkRunningAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        StepAttemptId attemptId,
        string workerId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkWaitingAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkCompletedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        SerializedPayload? result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkFailedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default);
}

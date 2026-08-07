using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.Steps;

namespace Spindle.Runtime.Tests.Stores;

internal sealed class CountingStepStore(
        IStepStore inner)
        : IStepStore
{
    public int CreateCalls { get; private set; }

    public int CreateManyCalls { get; private set; }

    public int CreatedInBatches { get; private set; }

    public int GetAsyncCalls { get; private set; }

    public int GetManyCalls { get; private set; }

    public int GetByFlowInstanceCalls { get; private set; }

    public int GetReadyStepsCalls { get; private set; }

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
        GetReadyStepsCalls = 0;
        MarkReadyCalls = 0;
        MarkRunningCalls = 0;
        MarkWaitingCalls = 0;
        MarkCompletedCalls = 0;
        MarkFailedCalls = 0;
        MarkDependentsReadyCalls = 0;
    }

    public ValueTask CreateAsync(
        StepInstanceRecord step,
        CancellationToken cancellationToken = default)
    {
        CreateCalls++;
        return inner.CreateAsync(step, cancellationToken);
    }

    public ValueTask CreateManyAsync(
        IReadOnlyList<StepInstanceRecord> steps,
        CancellationToken cancellationToken = default)
    {
        CreateManyCalls++;
        CreatedInBatches += steps.Count;
        return inner.CreateManyAsync(steps, cancellationToken);
    }

    public ValueTask<StepInstanceRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        CancellationToken cancellationToken = default)
    {
        GetAsyncCalls++;
        return inner.GetAsync(flowInstanceId, stepId, cancellationToken);
    }

    public ValueTask<IReadOnlyList<StepInstanceRecord>> GetManyAsync(
        FlowInstanceId flowInstanceId,
        IReadOnlyList<StepId> stepIds,
        CancellationToken cancellationToken = default)
    {
        GetManyCalls++;
        return inner.GetManyAsync(flowInstanceId, stepIds, cancellationToken);
    }

    public ValueTask<IReadOnlyList<StepInstanceRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        GetByFlowInstanceCalls++;
        return inner.GetByFlowInstanceAsync(flowInstanceId, cancellationToken);
    }

    public ValueTask<IReadOnlyList<StepInstanceRecord>> GetReadyStepsAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        GetReadyStepsCalls++;
        return inner.GetReadyStepsAsync(maxCount, cancellationToken);
    }

    public ValueTask MarkReadyAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        MarkReadyCalls++;
        return inner.MarkReadyAsync(flowInstanceId, stepId, updatedAt, cancellationToken);
    }

    public ValueTask MarkRunningAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        StepAttemptId attemptId,
        string workerId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        MarkRunningCalls++;
        return inner.MarkRunningAsync(flowInstanceId, stepId, attemptId, workerId, startedAt, cancellationToken);
    }

    public ValueTask MarkWaitingAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        MarkWaitingCalls++;
        return inner.MarkWaitingAsync(flowInstanceId, stepId, updatedAt, cancellationToken);
    }

    public ValueTask MarkCompletedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        int attempt,
        SerializedPayload? result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        MarkCompletedCalls++;
        return inner.MarkCompletedAsync(flowInstanceId, stepId, attempt, result, completedAt, cancellationToken);
    }

    public ValueTask MarkFailedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        int attempt,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default)
    {
        MarkFailedCalls++;
        return inner.MarkFailedAsync(flowInstanceId, stepId, attempt, error, failedAt, retryAt, cancellationToken);
    }

    public ValueTask MarkDependentsReadyAsync(
        FlowInstanceId flowInstanceId,
        List<StepId>? updatedSteps,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        MarkDependentsReadyCalls++;
        return inner.MarkDependentsReadyAsync(flowInstanceId, updatedSteps, updatedAt, cancellationToken);
    }

}

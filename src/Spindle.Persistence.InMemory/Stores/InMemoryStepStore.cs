using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.Steps;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemoryStepStore : IStepStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(FlowInstanceId FlowInstanceId, StepId StepId), StepInstanceRecord> _steps = [];
    private readonly List<StepAttemptRecord> _attempts = [];

    public ValueTask CreateAsync(
        StepInstanceRecord step,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (step.FlowInstanceId, step.StepId);

            if (_steps.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Step '{step.StepId}' already exists for flow instance '{step.FlowInstanceId}'.");
            }

            _steps.Add(key, InMemoryRecordCopies.Copy(step));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CreateManyAsync(
        IReadOnlyList<StepInstanceRecord> steps,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(steps);

        lock (_gate)
        {
            var keys = new HashSet<(FlowInstanceId FlowInstanceId, StepId StepId)>();

            foreach (var step in steps)
            {
                var key = (step.FlowInstanceId, step.StepId);

                if (!keys.Add(key) || _steps.ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        $"Step '{step.StepId}' already exists for flow instance '{step.FlowInstanceId}'.");
                }
            }

            foreach (var step in steps)
            {
                _steps.Add((step.FlowInstanceId, step.StepId), InMemoryRecordCopies.Copy(step));
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<StepInstanceRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _steps.TryGetValue((flowInstanceId, stepId), out var step)
                    ? InMemoryRecordCopies.Copy(step)
                    : null);
        }
    }

    public ValueTask<IReadOnlyList<StepInstanceRecord>> GetManyAsync(
        FlowInstanceId flowInstanceId,
        IReadOnlyList<StepId> stepIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(stepIds);

        lock (_gate)
        {
            var steps = stepIds
                .Distinct()
                .Select(stepId => _steps.TryGetValue((flowInstanceId, stepId), out var step)
                    ? InMemoryRecordCopies.Copy(step)
                    : null)
                .Where(step => step is not null)
                .Cast<StepInstanceRecord>()
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<StepInstanceRecord>>(steps);
        }
    }

    public ValueTask<IReadOnlyList<StepInstanceRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var steps = _steps.Values
                .Where(step => step.FlowInstanceId == flowInstanceId)
                .OrderBy(step => step.CreatedAt)
                .ThenBy(step => step.StepId.Value, StringComparer.Ordinal)
                .Select(InMemoryRecordCopies.Copy)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<StepInstanceRecord>>(steps);
        }
    }

    public ValueTask<IReadOnlyList<StepInstanceRecord>> GetReadyStepsAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var steps = _steps.Values
                .Where(step => step.Status == StepStatus.Ready)
                .OrderBy(step => step.CreatedAt)
                .ThenBy(step => step.StepId.Value, StringComparer.Ordinal)
                .Take(maxCount)
                .Select(InMemoryRecordCopies.Copy)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<StepInstanceRecord>>(steps);
        }
    }

    public ValueTask MarkReadyAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var step = GetRequired(flowInstanceId, stepId);

            if (step.Status is StepStatus.Completed or StepStatus.Failed or StepStatus.Cancelled)
            {
                return ValueTask.CompletedTask;
            }

            _steps[(flowInstanceId, stepId)] = step with
            {
                Status = StepStatus.Ready,
                UpdatedAt = updatedAt
            };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkRunningAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        StepAttemptId attemptId,
        string workerId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var step = GetRequired(flowInstanceId, stepId);
            var nextAttempt = step.Attempt + 1;

            _steps[(flowInstanceId, stepId)] = step with
            {
                Status = StepStatus.Running,
                Attempt = nextAttempt,
                StartedAt = startedAt,
                UpdatedAt = startedAt
            };

            _attempts.Add(new StepAttemptRecord
            {
                FlowInstanceId = flowInstanceId,
                StepId = stepId,
                AttemptId = attemptId,
                Attempt = nextAttempt,
                WorkerId = workerId,
                Status = StepStatus.Running,
                StartedAt = startedAt
            });
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkWaitingAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var step = GetRequired(flowInstanceId, stepId);
            _steps[(flowInstanceId, stepId)] = step with
            {
                Status = StepStatus.Waiting,
                UpdatedAt = updatedAt
            };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkCompletedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        SerializedPayload? result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var step = GetRequired(flowInstanceId, stepId);
            _steps[(flowInstanceId, stepId)] = step with
            {
                Status = StepStatus.Completed,
                Result = InMemoryRecordCopies.Copy(result),
                Error = null,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };

            CompleteLatestAttempt(flowInstanceId, stepId, StepStatus.Completed, completedAt, null);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var step = GetRequired(flowInstanceId, stepId);
            _steps[(flowInstanceId, stepId)] = step with
            {
                Status = StepStatus.Failed,
                Error = error,
                RetryAt = retryAt,
                CompletedAt = failedAt,
                UpdatedAt = failedAt
            };

            CompleteLatestAttempt(flowInstanceId, stepId, StepStatus.Failed, failedAt, error);
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<StepAttemptRecord> GetAttempts()
    {
        lock (_gate)
        {
            return _attempts.ToArray();
        }
    }

    private StepInstanceRecord GetRequired(FlowInstanceId flowInstanceId, StepId stepId)
    {
        return _steps.TryGetValue((flowInstanceId, stepId), out var step)
            ? step
            : throw new InvalidOperationException(
                $"Step '{stepId}' does not exist for flow instance '{flowInstanceId}'.");
    }

    private void CompleteLatestAttempt(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        StepStatus status,
        DateTimeOffset completedAt,
        string? error)
    {
        var attemptIndex = _attempts.FindLastIndex(attempt =>
            attempt.FlowInstanceId == flowInstanceId &&
            attempt.StepId == stepId &&
            attempt.CompletedAt is null);

        if (attemptIndex < 0)
        {
            return;
        }

        _attempts[attemptIndex] = _attempts[attemptIndex] with
        {
            Status = status,
            CompletedAt = completedAt,
            Error = error
        };
    }
}

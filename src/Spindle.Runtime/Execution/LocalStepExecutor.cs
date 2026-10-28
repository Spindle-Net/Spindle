using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Nodes;
using Spindle.Persistence.Timers;
using Spindle.Runtime;
using System.Diagnostics;

namespace Spindle;

internal sealed class LocalStepExecutor(
    ISpindleStore store,
    ISpindleSerializer serializer,
    TimeProvider timeProvider,
    TimeSpan leaseDuration,
    IServiceProvider services,
    ILogger? logger,
    string workerId) : IStepExecutor
{
    public bool SupportsDispatchMode(StepDispatchMode mode) =>
        mode is StepDispatchMode.Immediate or StepDispatchMode.LocalWorker;

    public async Task<StepExecutionResult> ExecuteAsync(
        FlowExecutionSession session,
        NodeInstanceRecord step,
        CancellationToken cancellationToken)
    {
        if (!session.TryGet(step.NodeId, out var registration))
        {
            return StepExecutionResult.NotExecuted;
        }

        if (step.DispatchMode == StepDispatchMode.Queued)
        {
            // TODO: Implement queued step dispatch with result consumer
            await store
                .ExecuteAsync(
                    (storeSession, storeCancellationToken) =>
                        storeSession.Nodes.MarkFailedAsync(
                            step.FlowInstanceId,
                            step.NodeId,
                            step.Attempt,
                            "Queued step dispatch is not supported yet.",
                            timeProvider.GetUtcNow(),
                            retryAt: null,
                            storeCancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            return StepExecutionResult.NotExecuted;
        }

        if (step.DispatchMode is not (StepDispatchMode.Immediate or StepDispatchMode.LocalWorker))
        {
            await store
                .ExecuteAsync(
                    (storeSession, storeCancellationToken) =>
                        storeSession.Nodes.MarkFailedAsync(
                            step.FlowInstanceId,
                            step.NodeId,
                            step.Attempt,
                            $"Step dispatch mode '{step.DispatchMode}' is not supported by the local runtime.",
                            timeProvider.GetUtcNow(),
                            retryAt: null,
                            storeCancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            return StepExecutionResult.NotExecuted;
        }

        var attemptId = new StepAttemptId(Guid.NewGuid().ToString("N"));
        var running = await TryStartAttemptAsync(step, attemptId, cancellationToken)
            .ConfigureAwait(false);

        if (running is null)
        {
            return StepExecutionResult.NotExecuted;
        }

        using var activity = Telemetry.ActivitySource.StartActivity($"{step.Name} - Attempt {running.Attempt}");
        activity?.SetTag("spindle.worker-id", workerId);

        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var context = new DefaultStepExecutionContext(
            step.FlowInstanceId,
            step.NodeId,
            attemptId,
            running.Attempt,
            services,
            new StepLogger(session, running, step, logger),
            attemptCancellation.Token);

        StepCallbackOutcome outcome;
        try
        {
            var inputs = await BuildInputsAsync(session, running, registration, cancellationToken)
                .ConfigureAwait(false);

            using (Telemetry.ActivitySource.StartActivity($"ExecuteStepCode - {step.NodeId}"))
            {
                outcome = await InvokeCallbackAsync(
                        registration,
                        inputs,
                        context,
                        attemptCancellation,
                        running.TimeoutPolicy,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = StepCallbackOutcome.Interrupted;
        }
        catch (Exception exception)
        {
            // Failing to prepare the attempt is a runtime problem, not a callback failure, so it is never retried.
            RecordException(activity, exception);
            return await FailAsync(running, exception.Message, NodeStatus.Failed, retryable: false)
                .ConfigureAwait(false);
        }

        try
        {
            switch (outcome.Kind)
            {
                case StepCallbackOutcomeKind.Succeeded when registration.Kind == NodeKind.ConditionWait:
                    if (outcome.Result is not bool conditionSatisfied)
                    {
                        throw new InvalidOperationException(
                            $"Condition node '{step.NodeId}' did not return a Boolean result.");
                    }

                    return await CompleteConditionCheckAsync(
                            session,
                            running,
                            conditionSatisfied,
                            cancellationToken)
                        .ConfigureAwait(false);

                case StepCallbackOutcomeKind.Succeeded:
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return await CompleteAsync(session, running, registration, outcome.Result)
                        .ConfigureAwait(false);

                case StepCallbackOutcomeKind.Faulted:
                    RecordException(activity, outcome.Exception!);
                    return await FailAsync(running, outcome.Exception!.Message, NodeStatus.Failed, retryable: true)
                        .ConfigureAwait(false);

                case StepCallbackOutcomeKind.TimedOut:
                    activity?.SetStatus(ActivityStatusCode.Error, "Timed out");
                    return await TimeOutAsync(session, running, registration)
                        .ConfigureAwait(false);

                case StepCallbackOutcomeKind.Interrupted:
                    await InterruptAsync(running).ConfigureAwait(false);
                    throw new OperationCanceledException(cancellationToken);

                default:
                    throw new InvalidOperationException($"Unknown step outcome '{outcome.Kind}'.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Serializing or persisting the outcome failed. This is not the callback's fault, so it is not retried.
            RecordException(activity, exception);
            return await FailAsync(running, exception.Message, NodeStatus.Failed, retryable: false)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Acquires the step lease and starts a new attempt if the step is still ready.
    /// </summary>
    /// <returns>The running step, or <c>null</c> if another worker owns it or it is no longer ready.</returns>
    private async Task<NodeInstanceRecord?> TryStartAttemptAsync(
        NodeInstanceRecord step,
        StepAttemptId attemptId,
        CancellationToken cancellationToken)
    {
        var acquiredAt = timeProvider.GetUtcNow();

        // Keep the lease valid while the attempt may legitimately run: its timeout plus the cleanup grace period.
        var duration = step.TimeoutPolicy is { } timeout
            ? StepPolicies.AddSaturating(
                leaseDuration,
                StepPolicies.AddSaturating(timeout.Timeout, timeout.CancellationGracePeriod))
            : leaseDuration;

        var running = await store
            .ExecuteAsync(
                async (storeSession, storeCancellationToken) =>
                {
                    var acquired = await storeSession.Leases
                        .TryAcquireStepLeaseAsync(
                            new StepLeaseRecord
                            {
                                FlowInstanceId = step.FlowInstanceId,
                                NodeId = step.NodeId,
                                Owner = workerId,
                                AcquiredAt = acquiredAt,
                                ExpiresAt = StepPolicies.AddSaturating(acquiredAt, duration)
                            },
                            storeCancellationToken)
                        .ConfigureAwait(false);

                    if (!acquired)
                    {
                        return null;
                    }

                    var current = await storeSession.Nodes
                        .GetAsync(step.FlowInstanceId, step.NodeId, storeCancellationToken)
                        .ConfigureAwait(false)
                        ?? step;

                    if (current.Status != NodeStatus.Ready)
                    {
                        await storeSession.Leases
                            .ReleaseStepLeaseAsync(
                                step.FlowInstanceId,
                                step.NodeId,
                                workerId,
                                storeCancellationToken)
                            .ConfigureAwait(false);
                        return null;
                    }

                    await storeSession.Nodes
                        .MarkRunningAsync(
                            step.FlowInstanceId,
                            step.NodeId,
                            attemptId,
                            workerId,
                            timeProvider.GetUtcNow(),
                            storeCancellationToken)
                        .ConfigureAwait(false);

                    return await storeSession.Nodes
                        .GetAsync(step.FlowInstanceId, step.NodeId, storeCancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return running?.Status == NodeStatus.Running ? running : null;
    }

    /// <summary>
    /// Runs the step callback, enforcing the timeout policy if there is one. Never throws for callback failures.
    /// </summary>
    private async Task<StepCallbackOutcome> InvokeCallbackAsync(
        StepExecutionRegistration registration,
        NodeInputs inputs,
        IStepExecutionContext context,
        CancellationTokenSource attemptCancellation,
        TimeoutPolicy? timeout,
        CancellationToken cancellationToken)
    {
        if (timeout is null)
        {
            return await ObserveAsync(InvokeAsync(registration, inputs, context), cancellationToken)
                .ConfigureAwait(false);
        }

        // Arm the deadline before invoking the callback so its synchronous part counts towards the timeout.
        using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadline = timeProvider.DelayAsync(timeout.Timeout, deadlineCancellation.Token);
        var callback = InvokeAsync(registration, inputs, context);

        if (await Task.WhenAny(callback, deadline).ConfigureAwait(false) == callback)
        {
            deadlineCancellation.Cancel();
            return await ObserveAsync(callback, cancellationToken).ConfigureAwait(false);
        }

        // Either the deadline elapsed or the caller cancelled. The attempt token is cancelled in both cases;
        // give the callback its grace period to clean up, then stop waiting for it.
        attemptCancellation.Cancel();
        await WaitForCleanupAsync(callback, timeout.CancellationGracePeriod).ConfigureAwait(false);

        return cancellationToken.IsCancellationRequested
            ? StepCallbackOutcome.Interrupted
            : StepCallbackOutcome.TimedOut;
    }

    private static async Task<object?> InvokeAsync(
        StepExecutionRegistration registration,
        NodeInputs inputs,
        IStepExecutionContext context)
    {
        // Async wrapper so exceptions thrown synchronously by the callback are captured in the task.
        return await registration.Execute(inputs, context).ConfigureAwait(false);
    }

    private static async Task<StepCallbackOutcome> ObserveAsync(
        Task<object?> callback,
        CancellationToken cancellationToken)
    {
        try
        {
            return StepCallbackOutcome.Succeeded(await callback.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StepCallbackOutcome.Interrupted;
        }
        catch (Exception exception)
        {
            return StepCallbackOutcome.Faulted(exception);
        }
    }

    private async Task WaitForCleanupAsync(
        Task<object?> callback,
        TimeSpan gracePeriod)
    {
        if (!callback.IsCompleted)
        {
            using var graceCancellation = new CancellationTokenSource();
            var grace = timeProvider.DelayAsync(gracePeriod, graceCancellation.Token);
            await Task.WhenAny(callback, grace).ConfigureAwait(false);
            graceCancellation.Cancel();
        }

        // The callback's result is discarded, but its failure must still be observed.
        _ = callback.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<StepExecutionResult> CompleteAsync(
        FlowExecutionSession session,
        NodeInstanceRecord running,
        StepExecutionRegistration registration,
        object? result)
    {
        var payload = SerializerReflection.Serialize(serializer, result, registration.ResultType);

        var completed = await PersistOutcomeAsync(
                running,
                async (storeSession, now, storeCancellationToken) =>
                {
                    await storeSession.Nodes
                        .MarkCompletedAsync(running.FlowInstanceId, running.NodeId, running.Attempt, payload, now, storeCancellationToken)
                        .ConfigureAwait(false);
                    await storeSession.Nodes
                        .MarkDependentsReadyAsync(running.FlowInstanceId, [running.NodeId], now, storeCancellationToken)
                        .ConfigureAwait(false);
                })
            .ConfigureAwait(false);

        if (completed.Status == NodeStatus.Completed)
        {
            session.SetResult(running.NodeId, result);
        }

        return completed;
    }

    /// <summary>
    /// Records a failed attempt. Schedules a retry when the failure is retryable and the retry budget allows
    /// it, otherwise ends the step with <paramref name="attemptOutcome"/>.
    /// </summary>
    private Task<StepExecutionResult> FailAsync(
        NodeInstanceRecord running,
        string error,
        NodeStatus attemptOutcome,
        bool retryable)
    {
        var failedAttempt = running.Attempt - running.RetryAttemptBaseline;
        var retry = retryable && StepPolicies.CanRetry(running.RetryPolicy, failedAttempt)
            ? running.RetryPolicy
            : null;

        return PersistOutcomeAsync(
            running,
            async (storeSession, now, storeCancellationToken) =>
            {
                if (retry is not null)
                {
                    var retryAt = StepPolicies.AddSaturating(
                        now,
                        StepPolicies.GetRetryDelay(retry, failedAttempt, Random.Shared.NextDouble()));

                    await storeSession.Nodes
                        .TryScheduleRetryAsync(running.FlowInstanceId, running.NodeId, running.Attempt, attemptOutcome, error, retryAt, now, storeCancellationToken)
                        .ConfigureAwait(false);
                    await storeSession.Timers
                        .CreateAsync(
                            new TimerRecord
                            {
                                FlowInstanceId = running.FlowInstanceId,
                                NodeId = running.NodeId,
                                DueAt = retryAt,
                                CreatedAt = now
                            },
                            storeCancellationToken)
                        .ConfigureAwait(false);
                }
                else if (attemptOutcome == NodeStatus.TimedOut)
                {
                    await storeSession.Nodes
                        .MarkTimedOutAsync(running.FlowInstanceId, running.NodeId, running.Attempt, error, now, storeCancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await storeSession.Nodes
                        .MarkFailedAsync(running.FlowInstanceId, running.NodeId, running.Attempt, error, now, retryAt: null, storeCancellationToken)
                        .ConfigureAwait(false);
                }
            });
    }

    private async Task<StepExecutionResult> TimeOutAsync(
        FlowExecutionSession session,
        NodeInstanceRecord running,
        StepExecutionRegistration registration)
    {
        var timeout = running.TimeoutPolicy!;
        var error = $"Step '{running.NodeId}' timed out after {timeout.Timeout}.";

        switch (timeout.Behavior)
        {
            case TimeoutBehavior.Fail:
                return await FailAsync(running, error, NodeStatus.TimedOut, retryable: true)
                    .ConfigureAwait(false);

            case TimeoutBehavior.Cancel:
                return await PersistOutcomeAsync(
                        running,
                        async (storeSession, now, storeCancellationToken) =>
                        {
                            await storeSession.Nodes
                                .MarkTimedOutAsync(running.FlowInstanceId, running.NodeId, running.Attempt, error, now, storeCancellationToken)
                                .ConfigureAwait(false);
                            await storeSession.FlowInstances
                                .UpdateStatusAsync(running.FlowInstanceId, FlowInstanceStatus.Cancelled, now, storeCancellationToken)
                                .ConfigureAwait(false);
                        })
                    .ConfigureAwait(false);

            case TimeoutBehavior.Continue:
                var result = registration.ResultType.IsValueType
                    ? Activator.CreateInstance(registration.ResultType)
                    : null;

                var payload = SerializerReflection.Serialize(serializer, result, registration.ResultType);
                var completed = await PersistOutcomeAsync(
                        running,
                        async (storeSession, now, storeCancellationToken) =>
                        {
                            // Close the attempt as timed out first; completing the node afterwards leaves the
                            // closed attempt untouched, so history shows the timeout and the node completes.
                            await storeSession.Nodes
                                .MarkTimedOutAsync(running.FlowInstanceId, running.NodeId, running.Attempt, error, now, storeCancellationToken)
                                .ConfigureAwait(false);
                            await storeSession.Nodes
                                .MarkCompletedAsync(running.FlowInstanceId, running.NodeId, running.Attempt, payload, now, storeCancellationToken)
                                .ConfigureAwait(false);
                            await storeSession.Nodes
                                .MarkDependentsReadyAsync(running.FlowInstanceId, [running.NodeId], now, storeCancellationToken)
                                .ConfigureAwait(false);
                        })
                    .ConfigureAwait(false);

                if (completed.Status == NodeStatus.Completed)
                {
                    session.SetResult(running.NodeId, result);
                }

                return completed;

            default:
                throw new InvalidOperationException($"Unknown timeout behavior '{timeout.Behavior}'.");
        }
    }

    private Task<StepExecutionResult> InterruptAsync(NodeInstanceRecord running)
        => PersistOutcomeAsync(
            running,
            async (storeSession, now, storeCancellationToken) =>
                await storeSession.Nodes
                    .TryInterruptAsync(running.FlowInstanceId, running.NodeId, running.Attempt, now, storeCancellationToken)
                    .ConfigureAwait(false));

    /// <summary>
    /// Persists the outcome of an attempt and releases its lease in one transaction.
    /// </summary>
    /// <remarks>
    /// The outcome is only written while the step is still running this attempt, so a late or duplicate
    /// outcome cannot overwrite newer state. It is persisted without the caller's cancellation token: the
    /// attempt has already finished, and dropping its outcome would leave the step stuck as running.
    /// </remarks>
    private async Task<StepExecutionResult> PersistOutcomeAsync(
        NodeInstanceRecord running,
        Func<ISpindleStoreSession, DateTimeOffset, CancellationToken, ValueTask> writeOutcome)
    {
        await ConcurrencyHelper.AquireLock(running.FlowInstanceId).ConfigureAwait(false);
        try
        {
            var node = await store
                .ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        var current = await storeSession.Nodes
                            .GetAsync(running.FlowInstanceId, running.NodeId, storeCancellationToken)
                            .ConfigureAwait(false);

                        if (current is { Status: NodeStatus.Running } && current.Attempt == running.Attempt)
                        {
                            await writeOutcome(storeSession, timeProvider.GetUtcNow(), storeCancellationToken)
                                .ConfigureAwait(false);
                        }

                        await storeSession.Leases
                            .ReleaseStepLeaseAsync(running.FlowInstanceId, running.NodeId, workerId, storeCancellationToken)
                            .ConfigureAwait(false);

                        return await storeSession.Nodes
                            .GetAsync(running.FlowInstanceId, running.NodeId, storeCancellationToken)
                            .ConfigureAwait(false);
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            return node is null
                ? StepExecutionResult.NotExecuted
                : StepExecutionResult.From(node);
        }
        finally
        {
            ConcurrencyHelper.ReleaseLock(running.FlowInstanceId);
        }
    }

    private static void RecordException(Activity? activity, Exception exception)
    {
        activity?.AddEvent(
            new ActivityEvent(
                "exception",
                tags: new ActivityTagsCollection
                {
                    { "exception.type", exception.GetType().FullName },
                    { "exception.message", exception.Message },
                    { "exception.stacktrace", exception.ToString() },
                }));
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    private async Task<StepExecutionResult> CompleteConditionCheckAsync(
        FlowExecutionSession session,
        NodeInstanceRecord condition,
        bool satisfied,
        CancellationToken cancellationToken)
    {
        var wait = await store.Conditions
            .GetAsync(condition.FlowInstanceId, condition.NodeId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Condition metadata for node '{condition.NodeId}' does not exist.");
        var now = timeProvider.GetUtcNow();

        if (wait.ExpiresAt is { } expiresAt && now >= expiresAt)
        {
            var error = $"Condition node '{condition.NodeId}' timed out at {expiresAt:O}.";
            await store.ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        await storeSession.Nodes.MarkTimedOutAsync(
                                condition.FlowInstanceId,
                                condition.NodeId,
                                condition.Attempt,
                                error,
                                now,
                                storeCancellationToken)
                            .ConfigureAwait(false);
                        await storeSession.Leases.ReleaseStepLeaseAsync(
                                condition.FlowInstanceId,
                                condition.NodeId,
                                workerId,
                                storeCancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return StepExecutionResult.TimedOut;
        }

        if (satisfied)
        {
            await store.ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        await storeSession.Nodes.MarkCompletedAsync(
                                condition.FlowInstanceId,
                                condition.NodeId,
                                condition.Attempt,
                                result: null,
                                now,
                                storeCancellationToken)
                            .ConfigureAwait(false);
                        await storeSession.Nodes.MarkDependentsReadyAsync(
                                condition.FlowInstanceId,
                                [condition.NodeId],
                                now,
                                storeCancellationToken)
                            .ConfigureAwait(false);
                        await storeSession.Leases.ReleaseStepLeaseAsync(
                                condition.FlowInstanceId,
                                condition.NodeId,
                                workerId,
                                storeCancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            session.SetResult(condition.NodeId, Unit.Value);
            return StepExecutionResult.Succeeded;
        }

        var dueAt = now.Add(wait.PollingInterval);
        if (wait.ExpiresAt is { } deadline && deadline < dueAt)
        {
            dueAt = deadline;
        }

        await store.ExecuteAsync(
                async (storeSession, storeCancellationToken) =>
                {
                    await storeSession.Nodes.MarkWaitingAsync(
                            condition.FlowInstanceId,
                            condition.NodeId,
                            condition.Attempt,
                            now,
                            storeCancellationToken)
                        .ConfigureAwait(false);
                    await storeSession.Timers.CreateAsync(
                            new TimerRecord
                            {
                                FlowInstanceId = condition.FlowInstanceId,
                                NodeId = condition.NodeId,
                                DueAt = dueAt,
                                CreatedAt = now
                            },
                            storeCancellationToken)
                        .ConfigureAwait(false);
                    await storeSession.Leases.ReleaseStepLeaseAsync(
                            condition.FlowInstanceId,
                            condition.NodeId,
                            workerId,
                            storeCancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return StepExecutionResult.Waiting;
    }

    private async ValueTask<NodeInputs> BuildInputsAsync(
        FlowExecutionSession session,
        Persistence.Nodes.NodeInstanceRecord step,
        StepExecutionRegistration registration,
        CancellationToken cancellationToken)
    {
        var values = new object?[step.Dependencies.Count];

        if (step.Dependencies.Count == 0)
        {
            return new NodeInputs(values);
        }

        var missingDependencyIds = new List<NodeId>();

        for (var i = 0; i < step.Dependencies.Count; i++)
        {
            var dependencyId = step.Dependencies[i];
            if (session.TryGetResult(dependencyId, out var cachedResult))
            {
                values[i] = NormalizeDependencyResult(
                    cachedResult,
                    GetDependencyResultType(registration, i));
                continue;
            }

            missingDependencyIds.Add(dependencyId);
        }

        if (missingDependencyIds.Count == 0)
        {
            return new NodeInputs(values);
        }

        var dependencies = await store.Nodes
            .GetManyAsync(step.FlowInstanceId, missingDependencyIds, cancellationToken)
            .ConfigureAwait(false);
        var dependenciesById = dependencies.ToDictionary(
            dependency => dependency.NodeId);

        for (var i = 0; i < step.Dependencies.Count; i++)
        {
            var dependencyId = step.Dependencies[i];
            if (values[i] is not null || session.TryGetResult(dependencyId, out _))
            {
                continue;
            }

            if (!dependenciesById.TryGetValue(dependencyId, out var dependency))
            {
                throw new InvalidOperationException(
                    $"Dependency step '{dependencyId}' does not exist for step '{step.NodeId}'.");
            }

            if (dependency.Status != NodeStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Dependency step '{dependencyId}' is not completed for step '{step.NodeId}'.");
            }

            var dependencyType = GetDependencyResultType(registration, i);

            var result = dependency.Result is null
                ? null
                : serializer.Deserialize(dependency.Result, dependencyType);
            values[i] = NormalizeDependencyResult(result, dependencyType);
        }

        return new NodeInputs(values);
    }

    private static Type GetDependencyResultType(
        StepExecutionRegistration registration,
        int index)
        => index < registration.DependencyResultTypes.Count
            ? registration.DependencyResultTypes[index]
            : typeof(object);

    private static object? NormalizeDependencyResult(
        object? result,
        Type dependencyType)
        => result is null && dependencyType == typeof(Unit)
            ? Unit.Value
            : result;
}

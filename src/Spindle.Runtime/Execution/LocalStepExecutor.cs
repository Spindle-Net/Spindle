using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
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
        var leaseOwner = $"{workerId}:{attemptId.Value}";
        var leaseAcquiredAt = timeProvider.GetUtcNow();
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
                                Owner = leaseOwner,
                                AcquiredAt = leaseAcquiredAt,
                                ExpiresAt = leaseAcquiredAt.Add(leaseDuration)
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
                                leaseOwner,
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
                        .ConfigureAwait(false)
                        ?? step;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (running is null || running.Status != NodeStatus.Running)
        {
            return StepExecutionResult.NotExecuted;
        }

        using var activity = Telemetry.ActivitySource.StartActivity($"{step.Name} - Attempt {running.Attempt}");
        activity?.SetTag("spindle.worker-id", workerId);
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var renewalCancellation = new CancellationTokenSource();
        var renewal = RenewLeaseAsync(running, leaseOwner, renewalCancellation.Token);
        try
        {
            var inputs = await BuildInputsAsync(session, running, registration, cancellationToken).ConfigureAwait(false);
            var invocation = ExecuteCallbackAsync(session, running, registration, attemptId, inputs, executionCancellation.Token);
            if (await Task.WhenAny(invocation, renewal).ConfigureAwait(false) == renewal)
            {
                executionCancellation.Cancel();
                try { await invocation.ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                await renewal.ConfigureAwait(false);
            }
            var outcome = await invocation.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (registration.Kind == NodeKind.ConditionWait && outcome.Exception is null)
            {
                if (outcome.Result is not bool satisfied)
                    throw new InvalidOperationException("Condition callback did not return a Boolean.");
                return await CompleteConditionCheckAsync(session, running, satisfied, leaseOwner, cancellationToken).ConfigureAwait(false);
            }

            var result = outcome.Result;
            var continueAfterTimeout = outcome.TimedOut && running.Timeout?.Behavior == Spindle.Abstractions.Policies.TimeoutBehavior.Continue;
            if (continueAfterTimeout)
                result = registration.ResultType.IsValueType ? Activator.CreateInstance(registration.ResultType) : null;
            // Serialization errors are infrastructure failures, outside the callback retry path.
            var payload = outcome.Exception is null || continueAfterTimeout
                ? SerializerReflection.Serialize(serializer, result, registration.ResultType) : null;
            var now = timeProvider.GetUtcNow();
            await ConcurrencyHelper.AquireLock(step.FlowInstanceId, cancellationToken).ConfigureAwait(false);
            StepExecutionResult executionResult;
            try
            {
                executionResult = await store.ExecuteAsync(async (storeSession, token) =>
                {
                    var flow = await storeSession.FlowInstances.GetAsync(step.FlowInstanceId, token);
                    if (flow is null || flow.Status is FlowInstanceStatus.Cancelled or FlowInstanceStatus.Completed)
                    {
                        await storeSession.Nodes.TryUpdateExecutionAsync(running with { Status = NodeStatus.Cancelled,
                            UpdatedAt = now, CompletedAt = now }, running.Attempt, NodeStatus.Running,
                            NodeStatus.Cancelled, token);
                        return new StepExecutionResult(true, NodeStatus.Cancelled);
                    }
                    var timedOut = outcome.TimedOut;
                    var attemptStatus = timedOut ? NodeStatus.TimedOut : outcome.Exception is null ? NodeStatus.Completed : NodeStatus.Failed;
                    var error = outcome.Exception?.Message;
                    var cancelFlow = timedOut && running.Timeout?.Behavior == Spindle.Abstractions.Policies.TimeoutBehavior.Cancel;
                    var succeeded = outcome.Exception is null || continueAfterTimeout;
                    var budgetAttempt = running.Attempt - running.RetryAttemptBaseline;
                    var retry = !succeeded && !cancelFlow && running.Kind == NodeKind.Step &&
                        running.Retry is { } policy && budgetAttempt < policy.MaxAttempts;
                    DateTimeOffset? retryAt = retry ? StepPolicy.DueAt(now, StepPolicy.Delay(running.Retry!, budgetAttempt)) : null;
                    var status = succeeded ? NodeStatus.Completed : retry ? NodeStatus.Waiting : attemptStatus;
                    var updated = running with { Status = status, Error = succeeded ? null : error,
                        RetryAt = retryAt, CompletedAt = retry ? null : now, UpdatedAt = now };
                    // Preserve timeout diagnostics on the attempt even when the node continues successfully.
                    if (!await storeSession.Nodes.TryUpdateExecutionAsync(updated with { Error = error }, running.Attempt,
                            NodeStatus.Running, attemptStatus, token)) return StepExecutionResult.NotExecuted;
                    if (succeeded)
                    {
                        await storeSession.Nodes.MarkCompletedAsync(step.FlowInstanceId, step.NodeId, -1, payload, now, token);
                        await storeSession.Nodes.MarkDependentsReadyAsync(step.FlowInstanceId, [step.NodeId], now, token);
                    }
                    if (retryAt is { } dueAt)
                        await storeSession.Timers.CreateAsync(new TimerRecord { FlowInstanceId = step.FlowInstanceId,
                            NodeId = step.NodeId, DueAt = dueAt, CreatedAt = now }, token);
                    if (cancelFlow)
                        await storeSession.FlowInstances.UpdateStatusAsync(step.FlowInstanceId, FlowInstanceStatus.Cancelled, now, token);
                    return new StepExecutionResult(true, status);
                }, cancellationToken).ConfigureAwait(false);
            }
            finally { ConcurrencyHelper.ReleaseLock(step.FlowInstanceId); }
            if (executionResult.Status == NodeStatus.Completed) session.SetResult(step.NodeId, result);
            activity?.SetStatus(executionResult.Status == NodeStatus.Completed ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                outcome.Exception?.Message);
            return executionResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await store.ExecuteAsync(async (storeSession, token) =>
            {
                var now = timeProvider.GetUtcNow();
                await storeSession.Nodes.TryUpdateExecutionAsync(running with { Status = NodeStatus.Ready,
                    RetryAttemptBaseline = running.RetryAttemptBaseline + 1, RetryAt = null,
                    CompletedAt = null, UpdatedAt = now }, running.Attempt, NodeStatus.Running, NodeStatus.Cancelled, token);
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            // Infrastructure failures are terminal and never consume a retry policy.
            await store.ExecuteAsync(async (storeSession, token) =>
            {
                var now = timeProvider.GetUtcNow();
                await storeSession.Nodes.TryUpdateExecutionAsync(running with { Status = NodeStatus.Failed,
                    Error = exception.Message, CompletedAt = now, UpdatedAt = now }, running.Attempt,
                    NodeStatus.Running, NodeStatus.Failed, token);
            }, CancellationToken.None).ConfigureAwait(false);
            logger?.LogError(exception, "Infrastructure failure for step {NodeId}", step.NodeId);
            return StepExecutionResult.Failed;
        }
        finally
        {
            renewalCancellation.Cancel();
            try { await renewal.ConfigureAwait(false); }
            catch (OperationCanceledException) when (renewalCancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Lease renewal failed for step {NodeId}", step.NodeId);
            }
            finally
            {
                await store.ExecuteAsync((storeSession, token) => storeSession.Leases.ReleaseStepLeaseAsync(
                    step.FlowInstanceId, step.NodeId, leaseOwner, token), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task RenewLeaseAsync(NodeInstanceRecord step, string leaseOwner, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromTicks(Math.Max(1, leaseDuration.Ticks / 2));
        while (true)
        {
            await Task.Delay(interval, timeProvider, cancellationToken).ConfigureAwait(false);
            var now = timeProvider.GetUtcNow();
            var acquired = await store.ExecuteAsync((session, token) => session.Leases.TryAcquireStepLeaseAsync(
                new StepLeaseRecord { FlowInstanceId = step.FlowInstanceId, NodeId = step.NodeId,
                    Owner = leaseOwner, AcquiredAt = now, ExpiresAt = now.Add(leaseDuration) }, token), cancellationToken);
            if (!acquired) throw new InvalidOperationException("Step lease was lost during execution.");
        }
    }

    private async Task<StepCallbackOutcome> ExecuteCallbackAsync(FlowExecutionSession session, NodeInstanceRecord running,
        StepExecutionRegistration registration, StepAttemptId attemptId, NodeInputs inputs, CancellationToken cancellationToken)
    {
        var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timerCancellation = new CancellationTokenSource();
        var context = new DefaultStepExecutionContext(running.FlowInstanceId, running.NodeId, attemptId, running.Attempt,
            services, new StepLogger(session, running, running, logger), attemptCancellation.Token);
        // Invoke independently so a synchronously blocking callback cannot prevent deadline handling.
        var callback = Task.Run(async () => await registration.Execute(inputs, context).ConfigureAwait(false), CancellationToken.None);
        var deadline = running.Timeout is { } timeout
            ? StepPolicy.DelayAsync(timeout.Timeout, timeProvider, timerCancellation.Token)
            : Task.Delay(Timeout.InfiniteTimeSpan, timerCancellation.Token);
        using var cancellationWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timerCancellation.Token);
        var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationWait.Token);
        try
        {
            var winner = await Task.WhenAny(callback, deadline, cancelled).ConfigureAwait(false);
            if (winner == callback)
            {
                try { return new StepCallbackOutcome(await callback.ConfigureAwait(false), null, false); }
                catch (Exception exception) { return new StepCallbackOutcome(null, exception, false); }
            }
            try { attemptCancellation.Cancel(); }
            catch (AggregateException exception)
            {
                logger?.LogWarning(exception, "Step cancellation callback threw for {NodeId}", running.NodeId);
            }
            var grace = running.Timeout?.CancellationGracePeriod ?? TimeSpan.FromSeconds(5);
            if (!callback.IsCompleted && grace > TimeSpan.Zero)
                await Task.WhenAny(callback, StepPolicy.DelayAsync(grace, timeProvider, timerCancellation.Token)).ConfigureAwait(false);
            _ = ObserveCallbackAsync(callback, attemptCancellation);
            attemptCancellation = null!; // The observer owns cleanup, including abandoned callbacks.
            cancellationToken.ThrowIfCancellationRequested();
            return new StepCallbackOutcome(null, new TimeoutException($"Step '{running.NodeId}' exceeded its execution timeout."), true);
        }
        finally
        {
            timerCancellation.Cancel();
            attemptCancellation?.Dispose();
        }
    }

    private static async Task ObserveCallbackAsync(Task<object?> callback, CancellationTokenSource cancellation)
    {
        try { await callback.ConfigureAwait(false); }
        catch (Exception) { /* Late outcomes must never mutate workflow state. */ }
        finally { cancellation.Dispose(); }
    }

    private async Task<StepExecutionResult> CompleteConditionCheckAsync(
        FlowExecutionSession session,
        NodeInstanceRecord condition,
        bool satisfied,
        string leaseOwner,
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
                                leaseOwner,
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
                                leaseOwner,
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
                            leaseOwner,
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

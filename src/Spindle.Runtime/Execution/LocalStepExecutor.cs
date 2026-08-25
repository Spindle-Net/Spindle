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
                                Owner = workerId,
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
                        .ConfigureAwait(false)
                        ?? step;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (running is null || running.Status != NodeStatus.Running)
        {
            return StepExecutionResult.NotExecuted;
        }

        using var activity = Telemetry.ActivitySource.StartActivity($"{step.Name} - Attempt {step.Attempt}");
        activity?.SetTag("spindle.worker-id", workerId);

        try
        {
            var stepLogger = new StepLogger(session, running, step, logger);
            var context = new DefaultStepExecutionContext(
                step.FlowInstanceId,
                step.NodeId,
                attemptId,
                running.Attempt,
                services,
                stepLogger,
                cancellationToken);

            var inputs = await BuildInputsAsync(session, running, registration, cancellationToken)
                .ConfigureAwait(false);

            // TODO: Make a difference of immediate and
            object? result;
            using (var executeActivity = Telemetry.ActivitySource.StartActivity($"ExecuteStepCode - {step.NodeId}"))
            {
                result = await registration.Execute(inputs, context)
                    .ConfigureAwait(false);
            }

            if (registration.Kind == NodeKind.ConditionWait)
            {
                if (result is not bool conditionSatisfied)
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
            }

            await ConcurrencyHelper.AquireLock(step.FlowInstanceId);
            await store
                .ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        await storeSession.Nodes
                            .MarkCompletedAsync(
                                step.FlowInstanceId,
                                step.NodeId,
                                running.Attempt,
                                SerializerReflection.Serialize(serializer, result, registration.ResultType),
                                timeProvider.GetUtcNow(),
                                storeCancellationToken)
                            .ConfigureAwait(false);

                        await storeSession.Nodes
                            .MarkDependentsReadyAsync(
                                step.FlowInstanceId,
                                [step.NodeId],
                                timeProvider.GetUtcNow(),
                                storeCancellationToken)
                            .ConfigureAwait(false);

                        await storeSession.Leases
                            .ReleaseStepLeaseAsync(
                                step.FlowInstanceId,
                                step.NodeId,
                                workerId,
                                storeCancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            ConcurrencyHelper.ReleaseLock(step.FlowInstanceId);

            session.SetResult(step.NodeId, result);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return StepExecutionResult.Succeeded;
        }
        catch (Exception exception)
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
            try
            {
                await store
                    .ExecuteAsync(
                        (storeSession, storeCancellationToken) =>
                            storeSession.Nodes.MarkFailedAsync(
                                step.FlowInstanceId,
                                step.NodeId,
                                running.Attempt,
                                exception.Message,
                                timeProvider.GetUtcNow(),
                                retryAt: null,
                                storeCancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await store
                    .ExecuteAsync(
                        (storeSession, storeCancellationToken) =>
                            storeSession.Leases.ReleaseStepLeaseAsync(
                                step.FlowInstanceId,
                                step.NodeId,
                                workerId,
                                storeCancellationToken),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        }

        return StepExecutionResult.Failed;
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

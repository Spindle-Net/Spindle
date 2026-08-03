using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Steps;

namespace Spindle;

internal sealed class LocalStepExecutor(
    ISpindleStore store,
    StepScheduler scheduler,
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
        StepInstanceRecord step,
        CancellationToken cancellationToken)
    {
        if (!session.TryGet(step.StepId, out var registration))
        {
            return StepExecutionResult.NotExecuted;
        }

        if (step.DispatchMode == StepDispatchMode.Queued)
        {
            // TODO: Implement queued step dispatch with result consumer
            await store
                .ExecuteAsync(
                    (storeSession, storeCancellationToken) =>
                        storeSession.Steps.MarkFailedAsync(
                            step.FlowInstanceId,
                            step.StepId,
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
                        storeSession.Steps.MarkFailedAsync(
                            step.FlowInstanceId,
                            step.StepId,
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
                                StepId = step.StepId,
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

                    var current = await storeSession.Steps
                        .GetAsync(step.FlowInstanceId, step.StepId, storeCancellationToken)
                        .ConfigureAwait(false)
                        ?? step;

                    if (current.Status != StepStatus.Ready)
                    {
                        await storeSession.Leases
                            .ReleaseStepLeaseAsync(
                                step.FlowInstanceId,
                                step.StepId,
                                workerId,
                                storeCancellationToken)
                            .ConfigureAwait(false);
                        return null;
                    }

                    await storeSession.Steps
                        .MarkRunningAsync(
                            step.FlowInstanceId,
                            step.StepId,
                            attemptId,
                            workerId,
                            timeProvider.GetUtcNow(),
                            storeCancellationToken)
                        .ConfigureAwait(false);

                    return await storeSession.Steps
                        .GetAsync(step.FlowInstanceId, step.StepId, storeCancellationToken)
                        .ConfigureAwait(false)
                        ?? step;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (running is null || running.Status != StepStatus.Running)
        {
            return StepExecutionResult.NotExecuted;
        }

        try
        {
            var stepLogger = new StepLogger(session, running, step, logger);
            var context = new DefaultStepExecutionContext(
                step.FlowInstanceId,
                step.StepId,
                attemptId,
                running.Attempt,
                services,
                stepLogger,
                cancellationToken);

            var inputs = await BuildInputsAsync(session, running, registration, cancellationToken)
                .ConfigureAwait(false);

            // TODO: Make a difference of immediate and
            var result = await registration.Execute(inputs, context)
                .ConfigureAwait(false);

            await store
                .ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        await storeSession.Steps
                            .MarkCompletedAsync(
                                step.FlowInstanceId,
                                step.StepId,
                                SerializerReflection.Serialize(serializer, result, registration.ResultType),
                                timeProvider.GetUtcNow(),
                                storeCancellationToken)
                            .ConfigureAwait(false);

                        await scheduler
                            .MarkDependentsReadyAsync(storeSession, step.FlowInstanceId, storeCancellationToken)
                            .ConfigureAwait(false);

                        await storeSession.Leases
                            .ReleaseStepLeaseAsync(
                                step.FlowInstanceId,
                                step.StepId,
                                workerId,
                                storeCancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            session.SetResult(step.StepId, result);

            return StepExecutionResult.Succeeded;
        }
        catch (Exception exception)
        {
            try
            {
                await store
                    .ExecuteAsync(
                        (storeSession, storeCancellationToken) =>
                            storeSession.Steps.MarkFailedAsync(
                                step.FlowInstanceId,
                                step.StepId,
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
                                step.StepId,
                                workerId,
                                storeCancellationToken),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        return StepExecutionResult.Failed;
    }

    private async ValueTask<StepInputs> BuildInputsAsync(
        FlowExecutionSession session,
        Persistence.Steps.StepInstanceRecord step,
        StepExecutionRegistration registration,
        CancellationToken cancellationToken)
    {
        var values = new object?[step.Dependencies.Count];

        if (step.Dependencies.Count == 0)
        {
            return new StepInputs(values);
        }

        var missingDependencyIds = new List<StepId>();

        for (var i = 0; i < step.Dependencies.Count; i++)
        {
            var dependencyId = step.Dependencies[i];
            if (session.TryGetResult(dependencyId, out var cachedResult))
            {
                values[i] = cachedResult;
                continue;
            }

            missingDependencyIds.Add(dependencyId);
        }

        if (missingDependencyIds.Count == 0)
        {
            return new StepInputs(values);
        }

        var dependencies = await store.Steps
            .GetManyAsync(step.FlowInstanceId, missingDependencyIds, cancellationToken)
            .ConfigureAwait(false);
        var dependenciesById = dependencies.ToDictionary(
            dependency => dependency.StepId);

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
                    $"Dependency step '{dependencyId}' does not exist for step '{step.StepId}'.");
            }

            if (dependency.Status != StepStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Dependency step '{dependencyId}' is not completed for step '{step.StepId}'.");
            }

            var dependencyType = i < registration.DependencyResultTypes.Count
                ? registration.DependencyResultTypes[i]
                : typeof(object);

            values[i] = dependency.Result is null
                ? null
                : serializer.Deserialize(dependency.Result, dependencyType);
        }

        return new StepInputs(values);
    }
}

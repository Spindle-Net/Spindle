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



    public async Task<bool> ExecuteAsync(
        FlowExecutionSession session,
        StepInstanceRecord step,
        CancellationToken cancellationToken)
    {
        if (!session.TryGet(step.StepId, out var registration))
        {
            return false;
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

            return false;
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

            return false;
        }

        var attemptId = new StepAttemptId(Guid.NewGuid().ToString("N"));
        var leaseAcquiredAt = timeProvider.GetUtcNow();

        var acquired = await store
            .ExecuteAsync(
                (storeSession, storeCancellationToken) =>
                    storeSession.Leases.TryAcquireStepLeaseAsync(
                        new StepLeaseRecord
                        {
                            FlowInstanceId = step.FlowInstanceId,
                            StepId = step.StepId,
                            Owner = workerId,
                            AcquiredAt = leaseAcquiredAt,
                            ExpiresAt = leaseAcquiredAt.Add(leaseDuration)
                        },
                        storeCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        if (!acquired)
        {
            return false;
        }

        try
        {
            var running = await store
                .ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        var current = await storeSession.Steps
                            .GetAsync(step.FlowInstanceId, step.StepId, storeCancellationToken)
                            .ConfigureAwait(false)
                            ?? step;

                        if (current.Status != StepStatus.Ready)
                        {
                            return current;
                        }

                        await storeSession.Steps
                            .MarkRunningAsync(step.FlowInstanceId, step.StepId, attemptId, workerId, timeProvider.GetUtcNow(), storeCancellationToken)
                            .ConfigureAwait(false);

                        return await storeSession.Steps
                            .GetAsync(step.FlowInstanceId, step.StepId, storeCancellationToken)
                            .ConfigureAwait(false)
                            ?? step;
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (running.Status != StepStatus.Running)
            {
                return false;
            }

            var stepLogger = new StepLogger(session, running, step, logger);
            var context = new DefaultStepExecutionContext(
                step.FlowInstanceId,
                step.StepId,
                attemptId,
                running.Attempt,
                services,
                stepLogger,
                cancellationToken);

            var inputs = await BuildInputsAsync(running, registration, cancellationToken)
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
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
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

        return true;
    }

    private async ValueTask<StepInputs> BuildInputsAsync(
        Persistence.Steps.StepInstanceRecord step,
        StepExecutionRegistration registration,
        CancellationToken cancellationToken)
    {
        var values = new object?[step.Dependencies.Count];

        if (step.Dependencies.Count == 0)
        {
            return new StepInputs(values);
        }

        var dependencies = await store.Steps
            .GetManyAsync(step.FlowInstanceId, step.Dependencies, cancellationToken)
            .ConfigureAwait(false);
        var dependenciesById = dependencies.ToDictionary(
            dependency => dependency.StepId);

        for (var i = 0; i < step.Dependencies.Count; i++)
        {
            var dependencyId = step.Dependencies[i];
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

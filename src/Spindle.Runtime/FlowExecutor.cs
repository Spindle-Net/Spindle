using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence;
using Spindle.Runtime;

namespace Spindle;

internal sealed class FlowExecutor(
    ISpindleStore store,
    FlowRegistry registry,
    ISpindleSerializer serializer,
    TimeProvider timeProvider,
    BarrierProcessor barrierProcessor,
    StepHandlerRegistry stepHandlers,
    IServiceProvider services)
{
    public async ValueTask ExecuteAsync(
        FlowInstanceId instanceId,
        FlowExecutionSession session,
        CancellationToken cancellationToken = default)
    {
        var instance = await store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Flow instance '{instanceId}' does not exist.");

        // Check if the status is terminal, in that case don't execute the step
        if (instance.Status is FlowInstanceStatus.Completed or FlowInstanceStatus.Failed or FlowInstanceStatus.Cancelled)
        {
            return;
        }

        var descriptor = registry.Resolve(instance.FlowName, instance.FlowVersion);

        await barrierProcessor.ProcessAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        var nodes = await store
            .ExecuteAsync(
                async (storeSession, storeCancellationToken) =>
                {
                    await storeSession.FlowInstances
                        .UpdateStatusAsync(
                            instanceId,
                            FlowInstanceStatus.Running,
                            timeProvider.GetUtcNow(),
                            storeCancellationToken)
                        .ConfigureAwait(false);

                    return await storeSession.Nodes
                        .GetByFlowInstanceAsync(instanceId, storeCancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        session.BeginReplay(nodes);

        try
        {
            var request = serializer.Deserialize(instance.Input, descriptor.RequestType);
            var context = new RuntimeFlowContext(
                store,
                session,
                descriptor,
                serializer,
                timeProvider,
                stepHandlers,
                services,
                cancellationToken);

            object? result;
            using (var descriptorActivity = Telemetry.ActivitySource.StartActivity("Execute Descriptor"))
            {
                result = await descriptor.Execute(context, request)
                    .ConfigureAwait(false);
            }

            using (var descriptorActivity = Telemetry.ActivitySource.StartActivity("Await async descriptor initialization"))
            {
                await session.WaitForAsyncDescriptorInitializationTasks();
            }

            await store
                .ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        await FlushPendingNodeDeclarationsAsync(session, storeSession, storeCancellationToken)
                            .ConfigureAwait(false);

                        await storeSession.FlowInstances.MarkCompletedAsync(
                                instanceId,
                                SerializerReflection.Serialize(serializer, result, descriptor.ResultType),
                                timeProvider.GetUtcNow(),
                                storeCancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FlowSuspendedException)
        {
            await store
                .ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        await FlushPendingNodeDeclarationsAsync(session, storeSession, storeCancellationToken)
                            .ConfigureAwait(false);

                        await storeSession.FlowInstances.UpdateStatusAsync(
                                instanceId,
                                FlowInstanceStatus.Waiting,
                                timeProvider.GetUtcNow(),
                                storeCancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await store
                .ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        await FlushPendingNodeDeclarationsAsync(session, storeSession, storeCancellationToken)
                            .ConfigureAwait(false);

                        await storeSession.FlowInstances.MarkFailedAsync(
                            instanceId,
                            exception.Message,
                            timeProvider.GetUtcNow(),
                            storeCancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async ValueTask FlushPendingNodeDeclarationsAsync(
        FlowExecutionSession session,
        ISpindleStoreSession storeSession,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity();
        var pending = session.GetPendingNodeDeclarations();

        if (pending.Count == 0)
        {
            return;
        }

        await storeSession.Nodes
            .CreateManyAsync(pending, cancellationToken)
            .ConfigureAwait(false);

        foreach (var initialization in session.GetPendingNodeInitializations())
        {
            switch (initialization)
            {
                case TimerNodeInitialization timer:
                    await storeSession.Timers.CreateAsync(timer.Timer, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SignalNodeInitialization signal:
                    await storeSession.Signals.CreateWaitAsync(signal.SignalWait, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ConditionNodeInitialization condition:
                    await storeSession.Conditions.CreateAsync(condition.ConditionWait, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }

        session.MarkNodeDeclarationsFlushed();
    }
}

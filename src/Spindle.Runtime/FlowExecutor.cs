using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence;

namespace Spindle;

internal sealed class FlowExecutor(
    ISpindleStore store,
    FlowRegistry registry,
    ISpindleSerializer serializer,
    TimeProvider timeProvider,
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

        var steps = await store
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

                    return await storeSession.Steps
                        .GetByFlowInstanceAsync(instanceId, storeCancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        session.BeginReplay(steps);

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

            var result = await descriptor.Execute(context, request)
                .ConfigureAwait(false);

            await store
                .ExecuteAsync(
                    async (storeSession, storeCancellationToken) =>
                    {
                        await FlushPendingStepDeclarationsAsync(session, storeSession, storeCancellationToken)
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
                        await FlushPendingStepDeclarationsAsync(session, storeSession, storeCancellationToken)
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
                        await FlushPendingStepDeclarationsAsync(session, storeSession, storeCancellationToken)
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

    private static async ValueTask FlushPendingStepDeclarationsAsync(
        FlowExecutionSession session,
        ISpindleStoreSession storeSession,
        CancellationToken cancellationToken)
    {
        var pending = session.GetPendingStepDeclarations();

        if (pending.Count == 0)
        {
            return;
        }

        await storeSession.Steps
            .CreateManyAsync(pending, cancellationToken)
            .ConfigureAwait(false);

        session.MarkStepDeclarationsFlushed();
    }
}

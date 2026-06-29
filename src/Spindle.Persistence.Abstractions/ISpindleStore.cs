namespace Spindle.Persistence;

public interface ISpindleStore
{
    FlowDefinitions.IFlowDefinitionStore FlowDefinitions { get; }

    FlowInstances.IFlowInstanceStore FlowInstances { get; }

    Steps.IStepStore Steps { get; }

    Timers.ITimerStore Timers { get; }

    Signals.ISignalStore Signals { get; }

    Messaging.IOutboxStore Outbox { get; }

    Messaging.IInboxStore Inbox { get; }

    Leases.ILeaseStore Leases { get; }

    History.IExecutionHistoryStore History { get; }

    ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ISpindleStoreSession, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);

    async ValueTask ExecuteAsync(
        Func<ISpindleStoreSession, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
                async (session, operationCancellationToken) =>
                {
                    await operation(session, operationCancellationToken)
                        .ConfigureAwait(false);

                    return true;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}

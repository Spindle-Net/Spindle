using Spindle.Persistence;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Steps;
using Spindle.Persistence.Timers;
using Spindle.Persistence.InMemory.Stores;

namespace Spindle.Persistence.InMemory;

public class InMemorySpindleStore : ISpindleStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly InMemorySpindleStoreSession _session;

    public InMemorySpindleStore()
    {
        _session = new InMemorySpindleStoreSession();
    }

    public IFlowDefinitionStore FlowDefinitions => _session.FlowDefinitions;

    public IFlowInstanceStore FlowInstances => _session.FlowInstances;

    public IStepStore Steps => _session.Steps;

    public ITimerStore Timers => _session.Timers;

    public ISignalStore Signals => _session.Signals;

    public IOutboxStore Outbox => _session.Outbox;

    public IInboxStore Inbox => _session.Inbox;

    public ILeaseStore Leases => _session.Leases;

    public IExecutionHistoryStore History => _session.History;

    public async ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ISpindleStoreSession, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await operation(_session, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ExecuteAsync(
        Func<ISpindleStoreSession, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

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

    private sealed class InMemorySpindleStoreSession : ISpindleStoreSession
    {
        public InMemorySpindleStoreSession()
        {
            FlowDefinitions = new InMemoryFlowDefinitionStore();
            FlowInstances = new InMemoryFlowInstanceStore();
            Steps = new InMemoryStepStore();
            Timers = new InMemoryTimerStore();
            Signals = new InMemorySignalStore();
            Outbox = new InMemoryOutboxStore();
            Inbox = new InMemoryInboxStore();
            Leases = new InMemoryLeaseStore();
            History = new InMemoryExecutionHistoryStore();
        }

        public IFlowDefinitionStore FlowDefinitions { get; }

        public IFlowInstanceStore FlowInstances { get; }

        public IStepStore Steps { get; }

        public ITimerStore Timers { get; }

        public ISignalStore Signals { get; }

        public IOutboxStore Outbox { get; }

        public IInboxStore Inbox { get; }

        public ILeaseStore Leases { get; }

        public IExecutionHistoryStore History { get; }
    }
}

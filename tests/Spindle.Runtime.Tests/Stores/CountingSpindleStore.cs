using Spindle.Persistence;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.Conditions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Nodes;
using Spindle.Persistence.Timers;

namespace Spindle.Runtime.Tests.Stores;

internal sealed class CountingSpindleStore : ISpindleStore
{
    private readonly ISpindleStore _inner;

    public CountingSpindleStore(
        ISpindleStore inner)
    {
        _inner = inner;
        Nodes = new CountingNodeStore(inner.Nodes);
    }

    public IFlowDefinitionStore FlowDefinitions => _inner.FlowDefinitions;

    public IFlowInstanceStore FlowInstances => _inner.FlowInstances;

    public CountingNodeStore Nodes { get; }

    INodeStore ISpindleStore.Nodes => Nodes;

    public ITimerStore Timers => _inner.Timers;

    public IConditionWaitStore Conditions => _inner.Conditions;

    public ISignalStore Signals => _inner.Signals;

    public IOutboxStore Outbox => _inner.Outbox;

    public IInboxStore Inbox => _inner.Inbox;

    public ILeaseStore Leases => _inner.Leases;

    public IExecutionHistoryStore History => _inner.History;

    public ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ISpindleStoreSession, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        return _inner.ExecuteAsync(
            (session, storeCancellationToken) =>
                operation(new CountingStoreSession(session, Nodes), storeCancellationToken),
            cancellationToken);
    }
}

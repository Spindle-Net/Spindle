using Spindle.Persistence;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Steps;
using Spindle.Persistence.Timers;

namespace Spindle.Runtime.Tests.Stores;

internal sealed class CountingStoreSession(
        ISpindleStoreSession inner,
        CountingStepStore steps)
        : ISpindleStoreSession
{
    public IFlowDefinitionStore FlowDefinitions => inner.FlowDefinitions;

    public IFlowInstanceStore FlowInstances => inner.FlowInstances;

    public IStepStore Steps => steps;

    public ITimerStore Timers => inner.Timers;

    public ISignalStore Signals => inner.Signals;

    public IOutboxStore Outbox => inner.Outbox;

    public IInboxStore Inbox => inner.Inbox;

    public ILeaseStore Leases => inner.Leases;

    public IExecutionHistoryStore History => inner.History;
}

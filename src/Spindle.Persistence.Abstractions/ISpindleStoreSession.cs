using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Nodes;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Timers;

namespace Spindle.Persistence;

public interface ISpindleStoreSession
{
    IFlowDefinitionStore FlowDefinitions { get; }

    IFlowInstanceStore FlowInstances { get; }

    INodeStore Nodes { get; }

    ITimerStore Timers { get; }

    ISignalStore Signals { get; }

    IOutboxStore Outbox { get; }

    IInboxStore Inbox { get; }

    ILeaseStore Leases { get; }

    IExecutionHistoryStore History { get; }
}

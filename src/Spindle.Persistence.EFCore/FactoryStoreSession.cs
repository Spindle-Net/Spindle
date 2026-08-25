using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.Conditions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Nodes;
using Spindle.Persistence.Timers;

namespace Spindle.Persistence.EFCore;

internal sealed class FactoryStoreSession : ISpindleStoreSession
{
    public FactoryStoreSession(EFCoreSpindleStore store)
    {
        FlowDefinitions = new FlowDefinitionStore(store);
        FlowInstances = new FlowInstanceStore(store);
        Nodes = new NodeStore(store);
        Timers = new TimerStore(store);
        Conditions = new ConditionWaitStore(store);
        Signals = new SignalStore(store);
        Outbox = new OutboxStore(store);
        Inbox = new InboxStore(store);
        Leases = new LeaseStore(store);
        History = new ExecutionHistoryStore(store);
    }

    public IFlowDefinitionStore FlowDefinitions { get; }
    public IFlowInstanceStore FlowInstances { get; }
    public INodeStore Nodes { get; }
    public ITimerStore Timers { get; }
    public IConditionWaitStore Conditions { get; }
    public ISignalStore Signals { get; }
    public IOutboxStore Outbox { get; }
    public IInboxStore Inbox { get; }
    public ILeaseStore Leases { get; }
    public IExecutionHistoryStore History { get; }

    private sealed class FlowDefinitionStore(EFCoreSpindleStore store) : IFlowDefinitionStore
    {
        public ValueTask UpsertAsync(FlowDefinitionRecord definition, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.FlowDefinitions.UpsertAsync(definition, token), cancellationToken);

        public ValueTask<FlowDefinitionRecord?> GetAsync(FlowName flowName, FlowVersion flowVersion, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.FlowDefinitions.GetAsync(flowName, flowVersion, token), cancellationToken);

        public ValueTask<IReadOnlyList<FlowDefinitionRecord>> GetByNameAsync(FlowName flowName, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.FlowDefinitions.GetByNameAsync(flowName, token), cancellationToken);
    }

    private sealed class FlowInstanceStore(EFCoreSpindleStore store) : IFlowInstanceStore
    {
        public ValueTask CreateAsync(FlowInstanceRecord instance, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.FlowInstances.CreateAsync(instance, token), cancellationToken);

        public ValueTask<FlowInstanceRecord?> GetAsync(FlowInstanceId instanceId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.FlowInstances.GetAsync(instanceId, token), cancellationToken);

        public ValueTask<FlowInstanceRecord?> GetByIdempotencyKeyAsync(FlowName flowName, string idempotencyKey, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.FlowInstances.GetByIdempotencyKeyAsync(flowName, idempotencyKey, token), cancellationToken);

        public ValueTask<IReadOnlyList<FlowInstanceRecord>> GetRunnableAsync(int maxCount, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.FlowInstances.GetRunnableAsync(maxCount, token), cancellationToken);

        public ValueTask UpdateStatusAsync(FlowInstanceId instanceId, FlowInstanceStatus status, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.FlowInstances.UpdateStatusAsync(instanceId, status, updatedAt, token), cancellationToken);

        public ValueTask MarkCompletedAsync(FlowInstanceId instanceId, SerializedPayload result, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.FlowInstances.MarkCompletedAsync(instanceId, result, completedAt, token), cancellationToken);

        public ValueTask MarkFailedAsync(FlowInstanceId instanceId, string error, DateTimeOffset failedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.FlowInstances.MarkFailedAsync(instanceId, error, failedAt, token), cancellationToken);
    }

    private sealed class NodeStore(EFCoreSpindleStore store) : INodeStore
    {
        public ValueTask CreateAsync(NodeInstanceRecord step, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Nodes.CreateAsync(step, token), cancellationToken);

        public ValueTask CreateManyAsync(IReadOnlyList<NodeInstanceRecord> steps, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Nodes.CreateManyAsync(steps, token), cancellationToken);

        public ValueTask<NodeInstanceRecord?> GetAsync(FlowInstanceId flowInstanceId, NodeId nodeId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Nodes.GetAsync(flowInstanceId, nodeId, token), cancellationToken);

        public ValueTask<IReadOnlyList<NodeInstanceRecord>> GetManyAsync(FlowInstanceId flowInstanceId, IReadOnlyList<NodeId> nodeIds, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Nodes.GetManyAsync(flowInstanceId, nodeIds, token), cancellationToken);

        public ValueTask<IReadOnlyList<NodeInstanceRecord>> GetByFlowInstanceAsync(FlowInstanceId flowInstanceId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Nodes.GetByFlowInstanceAsync(flowInstanceId, token), cancellationToken);

        public ValueTask<IReadOnlyList<NodeInstanceRecord>> GetReadyNodesAsync(int maxCount, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Nodes.GetReadyNodesAsync(maxCount, token), cancellationToken);

        public ValueTask MarkReadyAsync(FlowInstanceId flowInstanceId, NodeId nodeId, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Nodes.MarkReadyAsync(flowInstanceId, nodeId, updatedAt, token), cancellationToken);

        public ValueTask MarkRunningAsync(FlowInstanceId flowInstanceId, NodeId nodeId, StepAttemptId attemptId, string workerId, DateTimeOffset startedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Nodes.MarkRunningAsync(flowInstanceId, nodeId, attemptId, workerId, startedAt, token), cancellationToken);

        public ValueTask MarkWaitingAsync(FlowInstanceId flowInstanceId, NodeId nodeId, int attempt, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Nodes.MarkWaitingAsync(flowInstanceId, nodeId, attempt, updatedAt, token), cancellationToken);

        public ValueTask MarkTimedOutAsync(FlowInstanceId flowInstanceId, NodeId nodeId, int attempt, string error, DateTimeOffset timedOutAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Nodes.MarkTimedOutAsync(flowInstanceId, nodeId, attempt, error, timedOutAt, token), cancellationToken);

        public ValueTask MarkCompletedAsync(FlowInstanceId flowInstanceId, NodeId nodeId, int attempt, SerializedPayload? result, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Nodes.MarkCompletedAsync(flowInstanceId, nodeId, attempt, result, completedAt, token), cancellationToken);

        public ValueTask MarkFailedAsync(FlowInstanceId flowInstanceId, NodeId nodeId, int attempt, string error, DateTimeOffset failedAt, DateTimeOffset? retryAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Nodes.MarkFailedAsync(flowInstanceId, nodeId, attempt, error, failedAt, retryAt, token), cancellationToken);

        public ValueTask MarkDependentsReadyAsync(FlowInstanceId flowInstanceId, List<NodeId>? updatedNodes, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Nodes.MarkDependentsReadyAsync(flowInstanceId, updatedNodes, updatedAt, token), cancellationToken);
    }

    private sealed class TimerStore(EFCoreSpindleStore store) : ITimerStore
    {
        public ValueTask CreateAsync(TimerRecord timer, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Timers.CreateAsync(timer, token), cancellationToken);

        public ValueTask<TimerRecord?> GetAsync(FlowInstanceId flowInstanceId, NodeId nodeId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Timers.GetAsync(flowInstanceId, nodeId, token), cancellationToken);

        public ValueTask<IReadOnlyList<TimerRecord>> GetDueAsync(DateTimeOffset dueAtOrBefore, int maxCount, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Timers.GetDueAsync(dueAtOrBefore, maxCount, token), cancellationToken);

        public ValueTask MarkFiredAsync(FlowInstanceId flowInstanceId, NodeId nodeId, DateTimeOffset firedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Timers.MarkFiredAsync(flowInstanceId, nodeId, firedAt, token), cancellationToken);
    }

    private sealed class ConditionWaitStore(EFCoreSpindleStore store) : IConditionWaitStore
    {
        public ValueTask CreateAsync(ConditionWaitRecord wait, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Conditions.CreateAsync(wait, token), cancellationToken);

        public ValueTask<ConditionWaitRecord?> GetAsync(FlowInstanceId flowInstanceId, NodeId nodeId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Conditions.GetAsync(flowInstanceId, nodeId, token), cancellationToken);
    }

    private sealed class SignalStore(EFCoreSpindleStore store) : ISignalStore
    {
        public ValueTask CreateWaitAsync(SignalWaitRecord wait, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Signals.CreateWaitAsync(wait, token), cancellationToken);

        public ValueTask<IReadOnlyList<SignalWaitRecord>> GetOpenWaitsAsync(SignalName signalName, CorrelationKey? correlationKey = null, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Signals.GetOpenWaitsAsync(signalName, correlationKey, token), cancellationToken);

        public ValueTask MarkWaitCompletedAsync(FlowInstanceId flowInstanceId, NodeId nodeId, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Signals.MarkWaitCompletedAsync(flowInstanceId, nodeId, completedAt, token), cancellationToken);

        public ValueTask AppendSignalAsync(SignalRecord signal, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Signals.AppendSignalAsync(signal, token), cancellationToken);
    }

    private sealed class OutboxStore(EFCoreSpindleStore store) : IOutboxStore
    {
        public ValueTask AddAsync(OutboxMessageRecord message, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Outbox.AddAsync(message, token), cancellationToken);

        public ValueTask<IReadOnlyList<OutboxMessageRecord>> GetUnpublishedAsync(int maxCount, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Outbox.GetUnpublishedAsync(maxCount, token), cancellationToken);

        public ValueTask MarkPublishedAsync(string messageId, DateTimeOffset publishedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Outbox.MarkPublishedAsync(messageId, publishedAt, token), cancellationToken);
    }

    private sealed class InboxStore(EFCoreSpindleStore store) : IInboxStore
    {
        public async ValueTask<bool> TryRecordAsync(
            InboxMessageRecord message,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await store
                    .ExecuteAsync(
                        (session, token) => session.Inbox.TryRecordAsync(message, token),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                var existing = await store
                    .ExecuteAsync(
                        (session, token) => session.Inbox.GetAsync(message.MessageId, token),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (existing != null)
                {
                    return false;
                }

                throw;
            }
        }

        public ValueTask<InboxMessageRecord?> GetAsync(string messageId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Inbox.GetAsync(messageId, token), cancellationToken);
    }

    private sealed class LeaseStore(EFCoreSpindleStore store) : ILeaseStore
    {
        public async ValueTask<bool> TryAcquireStepLeaseAsync(
            StepLeaseRecord lease,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await TryAcquireAsync(lease, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                return await TryAcquireAsync(lease, cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask ReleaseStepLeaseAsync(FlowInstanceId flowInstanceId, NodeId nodeId, string owner, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Leases.ReleaseStepLeaseAsync(flowInstanceId, nodeId, owner, token), cancellationToken);

        private ValueTask<bool> TryAcquireAsync(
            StepLeaseRecord lease,
            CancellationToken cancellationToken)
        {
            return store.ExecuteAsync(
                (session, token) => session.Leases.TryAcquireStepLeaseAsync(lease, token),
                cancellationToken);
        }
    }

    private sealed class ExecutionHistoryStore(EFCoreSpindleStore store) : IExecutionHistoryStore
    {
        public ValueTask AppendAsync(ExecutionHistoryRecord record, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.History.AppendAsync(record, token), cancellationToken);

        public ValueTask<IReadOnlyList<ExecutionHistoryRecord>> GetByFlowInstanceAsync(FlowInstanceId flowInstanceId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.History.GetByFlowInstanceAsync(flowInstanceId, token), cancellationToken);
    }
}

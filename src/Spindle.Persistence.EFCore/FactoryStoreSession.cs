using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Steps;
using Spindle.Persistence.Timers;

namespace Spindle.Persistence.EFCore;

internal sealed class FactoryStoreSession : ISpindleStoreSession
{
    public FactoryStoreSession(EFCoreSpindleStore store)
    {
        FlowDefinitions = new FlowDefinitionStore(store);
        FlowInstances = new FlowInstanceStore(store);
        Steps = new StepStore(store);
        Timers = new TimerStore(store);
        Signals = new SignalStore(store);
        Outbox = new OutboxStore(store);
        Inbox = new InboxStore(store);
        Leases = new LeaseStore(store);
        History = new ExecutionHistoryStore(store);
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

    private sealed class StepStore(EFCoreSpindleStore store) : IStepStore
    {
        public ValueTask CreateAsync(StepInstanceRecord step, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Steps.CreateAsync(step, token), cancellationToken);

        public ValueTask CreateManyAsync(IReadOnlyList<StepInstanceRecord> steps, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Steps.CreateManyAsync(steps, token), cancellationToken);

        public ValueTask<StepInstanceRecord?> GetAsync(FlowInstanceId flowInstanceId, StepId stepId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Steps.GetAsync(flowInstanceId, stepId, token), cancellationToken);

        public ValueTask<IReadOnlyList<StepInstanceRecord>> GetManyAsync(FlowInstanceId flowInstanceId, IReadOnlyList<StepId> stepIds, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Steps.GetManyAsync(flowInstanceId, stepIds, token), cancellationToken);

        public ValueTask<IReadOnlyList<StepInstanceRecord>> GetByFlowInstanceAsync(FlowInstanceId flowInstanceId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Steps.GetByFlowInstanceAsync(flowInstanceId, token), cancellationToken);

        public ValueTask<IReadOnlyList<StepInstanceRecord>> GetReadyStepsAsync(int maxCount, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Steps.GetReadyStepsAsync(maxCount, token), cancellationToken);

        public ValueTask MarkReadyAsync(FlowInstanceId flowInstanceId, StepId stepId, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Steps.MarkReadyAsync(flowInstanceId, stepId, updatedAt, token), cancellationToken);

        public ValueTask MarkRunningAsync(FlowInstanceId flowInstanceId, StepId stepId, StepAttemptId attemptId, string workerId, DateTimeOffset startedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Steps.MarkRunningAsync(flowInstanceId, stepId, attemptId, workerId, startedAt, token), cancellationToken);

        public ValueTask MarkWaitingAsync(FlowInstanceId flowInstanceId, StepId stepId, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Steps.MarkWaitingAsync(flowInstanceId, stepId, updatedAt, token), cancellationToken);

        public ValueTask MarkCompletedAsync(FlowInstanceId flowInstanceId, StepId stepId, SerializedPayload? result, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Steps.MarkCompletedAsync(flowInstanceId, stepId, result, completedAt, token), cancellationToken);

        public ValueTask MarkFailedAsync(FlowInstanceId flowInstanceId, StepId stepId, string error, DateTimeOffset failedAt, DateTimeOffset? retryAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Steps.MarkFailedAsync(flowInstanceId, stepId, error, failedAt, retryAt, token), cancellationToken);
    }

    private sealed class TimerStore(EFCoreSpindleStore store) : ITimerStore
    {
        public ValueTask CreateAsync(TimerRecord timer, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Timers.CreateAsync(timer, token), cancellationToken);

        public ValueTask<TimerRecord?> GetAsync(FlowInstanceId flowInstanceId, StepId stepId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Timers.GetAsync(flowInstanceId, stepId, token), cancellationToken);

        public ValueTask<IReadOnlyList<TimerRecord>> GetDueAsync(DateTimeOffset dueAtOrBefore, int maxCount, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Timers.GetDueAsync(dueAtOrBefore, maxCount, token), cancellationToken);

        public ValueTask MarkFiredAsync(FlowInstanceId flowInstanceId, StepId stepId, DateTimeOffset firedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Timers.MarkFiredAsync(flowInstanceId, stepId, firedAt, token), cancellationToken);
    }

    private sealed class SignalStore(EFCoreSpindleStore store) : ISignalStore
    {
        public ValueTask CreateWaitAsync(SignalWaitRecord wait, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Signals.CreateWaitAsync(wait, token), cancellationToken);

        public ValueTask<IReadOnlyList<SignalWaitRecord>> GetOpenWaitsAsync(SignalName signalName, CorrelationKey? correlationKey = null, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Signals.GetOpenWaitsAsync(signalName, correlationKey, token), cancellationToken);

        public ValueTask MarkWaitCompletedAsync(FlowInstanceId flowInstanceId, StepId stepId, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Signals.MarkWaitCompletedAsync(flowInstanceId, stepId, completedAt, token), cancellationToken);

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
        public ValueTask<bool> TryRecordAsync(InboxMessageRecord message, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Inbox.TryRecordAsync(message, token), cancellationToken);

        public ValueTask<InboxMessageRecord?> GetAsync(string messageId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Inbox.GetAsync(messageId, token), cancellationToken);
    }

    private sealed class LeaseStore(EFCoreSpindleStore store) : ILeaseStore
    {
        public ValueTask<bool> TryAcquireStepLeaseAsync(StepLeaseRecord lease, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.Leases.TryAcquireStepLeaseAsync(lease, token), cancellationToken);

        public ValueTask ReleaseStepLeaseAsync(FlowInstanceId flowInstanceId, StepId stepId, string owner, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.Leases.ReleaseStepLeaseAsync(flowInstanceId, stepId, owner, token), cancellationToken);
    }

    private sealed class ExecutionHistoryStore(EFCoreSpindleStore store) : IExecutionHistoryStore
    {
        public ValueTask AppendAsync(ExecutionHistoryRecord record, CancellationToken cancellationToken = default) =>
            store.ExecuteDirectAsync((session, token) => session.History.AppendAsync(record, token), cancellationToken);

        public ValueTask<IReadOnlyList<ExecutionHistoryRecord>> GetByFlowInstanceAsync(FlowInstanceId flowInstanceId, CancellationToken cancellationToken = default) =>
            store.ExecuteAsync((session, token) => session.History.GetByFlowInstanceAsync(flowInstanceId, token), cancellationToken);
    }
}

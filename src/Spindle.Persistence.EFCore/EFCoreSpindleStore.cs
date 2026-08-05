using Microsoft.EntityFrameworkCore;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Steps;
using Spindle.Persistence.Timers;
using Spindle.Persistence.EFCore.Stores;

namespace Spindle.Persistence.EFCore;

public sealed class EFCoreSpindleStore : ISpindleStore
{
    private readonly IDbContextFactory<SpindleDbContext> _contextFactory;
    private readonly FactoryStoreSession _rootSession;

    public EFCoreSpindleStore(
        IDbContextFactory<SpindleDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
        _rootSession = new FactoryStoreSession(this);
    }

    public IFlowDefinitionStore FlowDefinitions => _rootSession.FlowDefinitions;

    public IFlowInstanceStore FlowInstances => _rootSession.FlowInstances;

    public IStepStore Steps => _rootSession.Steps;

    public ITimerStore Timers => _rootSession.Timers;

    public ISignalStore Signals => _rootSession.Signals;

    public IOutboxStore Outbox => _rootSession.Outbox;

    public IInboxStore Inbox => _rootSession.Inbox;

    public ILeaseStore Leases => _rootSession.Leases;

    public IExecutionHistoryStore History => _rootSession.History;

    public async ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ISpindleStoreSession, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
            var session = new ContextStoreSession(context);

            var result = await operation(session, cancellationToken)
                .ConfigureAwait(false);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        });
    }

    internal async ValueTask ExecuteDirectAsync(
        Func<ISpindleStoreSession, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                async (session, token) =>
                {
                    await operation(session, token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class ContextStoreSession : ISpindleStoreSession
    {
        public ContextStoreSession(SpindleDbContext context)
        {
            FlowDefinitions = new EFCoreFlowDefinitionStore(context);
            FlowInstances = new EFCoreFlowInstanceStore(context);
            Steps = new EFCoreStepStore(context);
            Timers = new EFCoreTimerStore(context);
            Signals = new EFCoreSignalStore(context);
            Outbox = new EFCoreOutboxStore(context);
            Inbox = new EFCoreInboxStore(context);
            Leases = new EFCoreLeaseStore(context);
            History = new EFCoreExecutionHistoryStore(context);
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

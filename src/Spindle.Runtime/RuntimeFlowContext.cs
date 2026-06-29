using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Waiting;
using Spindle.Persistence;
using Spindle.Persistence.Steps;
using Spindle.Persistence.Timers;

namespace Spindle;

internal sealed class RuntimeFlowContext(
    ISpindleStore store,
    FlowExecutionSession session,
    FlowDescriptor descriptor,
    ISpindleSerializer serializer,
    TimeProvider timeProvider,
    StepHandlerRegistry stepHandlers,
    IServiceProvider services,
    CancellationToken cancellationToken)
    : IFlowContext
{
    public FlowInstanceId InstanceId => session.FlowInstanceId;

    public FlowName FlowName => descriptor.FlowName;

    public FlowVersion FlowVersion => descriptor.FlowVersion;

    public CancellationToken CancellationToken => cancellationToken;

    public Step<TResult> Step<TResult>(
        string id,
        string name,
        IReadOnlyList<Step> dependencies,
        StepCallback<TResult> execute,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(execute);

        return DeclareStep(
            id,
            name,
            StepKind.Step,
            handlerId: null,
            dependencies,
            execute,
            options);
    }

    public Step<TResult> StepHandler<TRequest, TResult>(
        string id,
        string name,
        StepHandlerId handlerId,
        IReadOnlyList<Step> dependencies,
        Func<StepInputs, TRequest> createRequest,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(createRequest);

        async ValueTask<TResult> Execute(StepInputs inputs, IStepExecutionContext context)
        {
            var handler = stepHandlers.Resolve<TRequest, TResult>(handlerId, services)
                ?? services.GetService(typeof(IStepHandler<TRequest, TResult>))
                    as IStepHandler<TRequest, TResult>;

            if (handler is null)
            {
                throw new NotSupportedException(
                    $"Step handler '{handlerId}' is not registered in the current service provider.");
            }

            return await handler.ExecuteAsync(createRequest(inputs), context)
                .ConfigureAwait(false);
        }

        return DeclareStep(
            id,
            name,
            StepKind.Step,
            handlerId,
            dependencies,
            Execute,
            options);
    }

    public async ValueTask WaitAll(params Step[] steps)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var step in steps)
        {
            if (!session.TryGetStep(step.Id, out var record))
            {
                throw new FlowSuspendedException();
            }

            if (record.Status == StepStatus.Completed)
            {
                continue;
            }

            if (record.Status == StepStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"Step '{step.Id}' failed: {record.Error}");
            }

            throw new FlowSuspendedException();
        }
    }

    public ValueTask Delay(
        string id,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var dueAt = timeProvider.GetUtcNow().Add(duration);
        return DelayUntil(id, dueAt, cancellationToken);
    }

    public async ValueTask DelayUntil(
        string id,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stepId = new StepId(id);

        var step = await store.ExecuteAsync(
                async (storeSession, storeCancellationToken) =>
                {
                    var timer = await storeSession.Timers
                        .GetAsync(InstanceId, stepId, storeCancellationToken)
                        .ConfigureAwait(false);

                    if (timer is null)
                    {
                        var now = timeProvider.GetUtcNow();

                        await storeSession.Timers
                            .CreateAsync(
                                new TimerRecord
                                {
                                    FlowInstanceId = InstanceId,
                                    StepId = stepId,
                                    DueAt = dueAt,
                                    CreatedAt = now
                                },
                                storeCancellationToken)
                            .ConfigureAwait(false);

                        if (!session.TryGetStep(stepId, out _))
                        {
                            var timerStep = new StepInstanceRecord
                            {
                                FlowInstanceId = InstanceId,
                                StepId = stepId,
                                Name = id,
                                Kind = StepKind.Timer,
                                Status = StepStatus.Waiting,
                                DispatchMode = StepDispatchMode.Immediate,
                                CreatedAt = now,
                                UpdatedAt = now
                            };

                            await storeSession.Steps
                                .CreateAsync(timerStep, storeCancellationToken)
                                .ConfigureAwait(false);

                            return timerStep;
                        }
                    }

                    return await storeSession.Steps
                        .GetAsync(InstanceId, stepId, storeCancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (step is null)
        {
            if (!session.TryGetStep(stepId, out step))
            {
                throw new InvalidOperationException(
                    $"Timer step '{stepId}' does not exist for flow instance '{InstanceId}'.");
            }
        }
        else
        {
            session.UpsertStep(step);
        }

        if (step.Status == StepStatus.Completed)
        {
            return;
        }

        if (step.Status == StepStatus.Failed)
        {
            throw new InvalidOperationException($"Timer step '{stepId}' failed: {step.Error}");
        }

        throw new FlowSuspendedException();
    }

    public ValueTask<TSignal> WaitForSignal<TSignal>(
        SignalName signalName,
        CorrelationKey? correlationKey = null,
        SignalWaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Signal waits are not supported by the local MVP runtime yet.");
    }

    private Step<TResult> DeclareStep<TResult>(
        string id,
        string name,
        StepKind kind,
        StepHandlerId? handlerId,
        IReadOnlyList<Step> dependencies,
        StepCallback<TResult> execute,
        StepOptions? options)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stepId = new StepId(id);
        var stepOptions = options ?? new StepOptions();
        var dependencyIds = dependencies.Select(dependency => dependency.Id).ToArray();
        var dependencyResultTypes = dependencies
            .Select(GetDependencyResultType)
            .ToArray();

        session.Register(stepId, dependencyResultTypes, execute);

        if (!session.TryGetStep(stepId, out _))
        {
            var now = timeProvider.GetUtcNow();
            var status = DependenciesCompleted(dependencyIds)
                ? StepStatus.Ready
                : StepStatus.Pending;

            session.TryDeclareStep(
                new StepInstanceRecord
                {
                    FlowInstanceId = InstanceId,
                    StepId = stepId,
                    Name = name,
                    Kind = kind,
                    Status = status,
                    HandlerId = handlerId,
                    Queue = stepOptions.Queue,
                    DispatchMode = stepOptions.DispatchMode,
                    Dependencies = dependencyIds,
                    CreatedAt = now,
                    UpdatedAt = now
                });
        }

        return new RuntimeStep<TResult>(
            store,
            session,
            serializer,
            InstanceId,
            stepId,
            name,
            kind,
            stepOptions);
    }

    private bool DependenciesCompleted(
        IReadOnlyList<StepId> dependencies)
    {
        foreach (var dependency in dependencies)
        {
            if (!session.TryGetStep(dependency, out var record) ||
                record.Status != StepStatus.Completed)
            {
                return false;
            }
        }

        return true;
    }

    private static Type GetDependencyResultType(Step dependency)
    {
        return dependency is IRuntimeStep runtimeStep
            ? runtimeStep.ResultType
            : typeof(object);
    }
}

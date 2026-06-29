using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Exceptions;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.Steps;

namespace Spindle;

public sealed class RuntimeSpindleRuntime : ISpindleRuntime
{
    private readonly ISpindleStore _store;
    private readonly FlowRegistry _registry;
    private readonly RuntimeSpindleOptions _options;
    private readonly ISpindleSerializer _serializer;
    private readonly TimeProvider _timeProvider;
    private readonly StepHandlerRegistry _stepHandlers;
    private readonly IServiceProvider _services;
    private readonly StepScheduler _scheduler;
    private readonly FlowExecutor _flowExecutor;
    private readonly StepExecutor _stepExecutor;
    private readonly ConcurrentDictionary<FlowInstanceId, SemaphoreSlim> _instanceGates = new();

    public RuntimeSpindleRuntime(
        ISpindleStore store,
        FlowRegistry? registry = null,
        RuntimeSpindleOptions? options = null)
    {
        _store = store;
        _registry = registry ?? new FlowRegistry();
        _options = options ?? new RuntimeSpindleOptions();
        _serializer = _options.Serializer ?? new JsonSpindleSerializer();
        _timeProvider = _options.TimeProvider ?? TimeProvider.System;
        _services = _options.Services ?? EmptyServiceProvider.Instance;
        _stepHandlers = _options.StepHandlers
            ?? _services.GetService(typeof(StepHandlerRegistry)) as StepHandlerRegistry
            ?? new StepHandlerRegistry();

        _scheduler = new StepScheduler(_timeProvider);
        _flowExecutor = new FlowExecutor(
            _store,
            _registry,
            _serializer,
            _timeProvider,
            _stepHandlers,
            _services);
        _stepExecutor = new StepExecutor(
            _store,
            _scheduler,
            _serializer,
            _timeProvider,
            _options.StepLeaseDuration,
            _services,
            _services.GetService(typeof(ILogger)) as ILogger,
            _options.WorkerId);
    }

    public RuntimeSpindleRuntime RegisterFlow<TRequest, TResult>(
        FlowName flowName,
        ISpindleFlow<TRequest, TResult> flow,
        FlowVersion? flowVersion = null)
    {
        _registry.Register(flowName, flow, flowVersion);
        return this;
    }

    public RuntimeSpindleRuntime RegisterFlow<TRequest, TResult>(
        FlowName flowName,
        Func<IFlowContext, TRequest, ValueTask<TResult>> run,
        FlowVersion? flowVersion = null)
    {
        _registry.Register(flowName, run, flowVersion);
        return this;
    }

    public ValueTask<FlowInstanceHandle<TResult>> StartAsync<TRequest, TResult>(
        FlowName flowName,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var version = options?.Version;
        return StartAsync<TRequest, TResult>(
            flowName,
            version ?? _registry.Resolve(flowName).FlowVersion,
            request,
            options,
            cancellationToken);
    }

    public async ValueTask<FlowInstanceHandle<TResult>> StartAsync<TRequest, TResult>(
        FlowName flowName,
        FlowVersion flowVersion,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = _registry.Resolve(flowName, flowVersion);
        var now = _timeProvider.GetUtcNow();
        var instanceId = new FlowInstanceId(Guid.NewGuid().ToString("N"));

        var (handle, created) = await _store
            .ExecuteAsync(
                async (storeSession, storeCancellationToken) =>
                {
                    if (options?.IdempotencyKey is { } idempotencyKey)
                    {
                        var existing = await storeSession.FlowInstances
                            .GetByIdempotencyKeyAsync(
                                flowName,
                                idempotencyKey,
                                storeCancellationToken)
                            .ConfigureAwait(false);

                        if (existing is not null)
                        {
                            return (new FlowInstanceHandle<TResult>
                            {
                                InstanceId = existing.InstanceId,
                                FlowName = existing.FlowName,
                                FlowVersion = existing.FlowVersion
                            }, Created: false);
                        }
                    }

                    await storeSession.FlowDefinitions
                        .UpsertAsync(
                            new FlowDefinitionRecord
                            {
                                FlowName = descriptor.FlowName,
                                FlowVersion = descriptor.FlowVersion,
                                DefinitionHash = descriptor.DefinitionHash,
                                FlowTypeName = descriptor.FlowType.AssemblyQualifiedName ?? descriptor.FlowType.FullName ?? descriptor.FlowType.Name,
                                CreatedAt = now,
                                UpdatedAt = now
                            },
                            storeCancellationToken)
                        .ConfigureAwait(false);

                    await storeSession.FlowInstances
                        .CreateAsync(
                            new Persistence.FlowInstances.FlowInstanceRecord
                            {
                                InstanceId = instanceId,
                                FlowName = descriptor.FlowName,
                                FlowVersion = descriptor.FlowVersion,
                                DefinitionHash = descriptor.DefinitionHash,
                                Status = FlowInstanceStatus.Running,
                                Input = _serializer.Serialize(request),
                                CorrelationKey = options?.CorrelationKey,
                                IdempotencyKey = options?.IdempotencyKey,
                                CreatedAt = now,
                                UpdatedAt = now
                            },
                            storeCancellationToken)
                        .ConfigureAwait(false);

                    return (new FlowInstanceHandle<TResult>
                    {
                        InstanceId = instanceId,
                        FlowName = descriptor.FlowName,
                        FlowVersion = descriptor.FlowVersion
                    }, Created: true);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (created)
        {
            await ExecuteWithInstanceGateAsync(
                    instanceId,
                    async gateCancellationToken =>
                    {
                        var session = new FlowExecutionSession(instanceId);
                        await _flowExecutor
                            .ExecuteAsync(instanceId, session, gateCancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return handle;
    }

    public async ValueTask<TResult> RunAsync<TRequest, TResult>(
        FlowName flowName,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var handle = await StartAsync<TRequest, TResult>(
                flowName,
                request,
                options,
                cancellationToken)
            .ConfigureAwait(false);

        var session = new FlowExecutionSession(handle.InstanceId);

        return await ExecuteWithInstanceGateAsync(
                handle.InstanceId,
                async gateCancellationToken =>
                {
                    for (var iteration = 0; iteration < _options.MaxRunIterations; iteration++)
                    {
                        var firedTimers = await FireDueTimersAsync(handle.InstanceId, gateCancellationToken)
                            .ConfigureAwait(false);

                        await _flowExecutor
                            .ExecuteAsync(handle.InstanceId, session, gateCancellationToken)
                            .ConfigureAwait(false);

                        var instance = await _store.FlowInstances
                            .GetAsync(handle.InstanceId, gateCancellationToken)
                            .ConfigureAwait(false)
                            ?? throw new InvalidOperationException($"Flow instance '{handle.InstanceId}' disappeared.");

                        if (instance.Status == FlowInstanceStatus.Completed)
                        {
                            if (instance.Result is null)
                            {
                                return default!;
                            }

                            return _serializer.Deserialize<TResult>(instance.Result);
                        }

                        if (instance.Status == FlowInstanceStatus.Failed)
                        {
                            throw new InvalidOperationException(
                                $"Flow '{instance.FlowName}' instance '{instance.InstanceId}' failed: {instance.Error}");
                        }

                        var executed = await _stepExecutor
                            .ExecuteReadyStepsAsync(session, cancellationToken: gateCancellationToken)
                            .ConfigureAwait(false);

                        firedTimers += await FireDueTimersAsync(handle.InstanceId, gateCancellationToken)
                            .ConfigureAwait(false);

                        if (executed == 0 && firedTimers == 0)
                        {
                            throw new NotSupportedException(
                                $"Flow '{instance.FlowName}' instance '{instance.InstanceId}' is waiting, but no local ready steps can be executed by this runtime.");
                        }
                    }

                    throw new InvalidOperationException(
                        $"Flow '{flowName}' did not complete within {_options.MaxRunIterations} local runtime iterations.");
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<int> FireDueTimersAsync(
        FlowInstanceId? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        return await _store
            .ExecuteAsync(
                async (storeSession, storeCancellationToken) =>
                {
                    var dueTimers = await storeSession.Timers
                        .GetDueAsync(now, maxCount: 100, storeCancellationToken)
                        .ConfigureAwait(false);

                    var fired = 0;

                    foreach (var timer in dueTimers)
                    {
                        if (instanceId is { } targetInstanceId &&
                            timer.FlowInstanceId != targetInstanceId)
                        {
                            continue;
                        }

                        var step = await storeSession.Steps
                            .GetAsync(timer.FlowInstanceId, timer.StepId, storeCancellationToken)
                            .ConfigureAwait(false);

                        if (step is null ||
                            step.Status is StepStatus.Completed
                                or StepStatus.Failed
                                or StepStatus.Cancelled
                                or StepStatus.TimedOut
                                or StepStatus.Skipped)
                        {
                            await storeSession.Timers
                                .MarkFiredAsync(timer.FlowInstanceId, timer.StepId, now, storeCancellationToken)
                                .ConfigureAwait(false);
                            continue;
                        }

                        await storeSession.Steps
                            .MarkCompletedAsync(timer.FlowInstanceId, timer.StepId, result: null, now, storeCancellationToken)
                            .ConfigureAwait(false);

                        await storeSession.Timers
                            .MarkFiredAsync(timer.FlowInstanceId, timer.StepId, now, storeCancellationToken)
                            .ConfigureAwait(false);

                        await _scheduler
                            .MarkDependentsReadyAsync(storeSession, timer.FlowInstanceId, storeCancellationToken)
                            .ConfigureAwait(false);

                        fired++;
                    }

                    return fired;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask ExecuteWithInstanceGateAsync(
        FlowInstanceId instanceId,
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken)
    {
        var gate = _instanceGates.GetOrAdd(
            instanceId,
            static _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<TResult> ExecuteWithInstanceGateAsync<TResult>(
        FlowInstanceId instanceId,
        Func<CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken)
    {
        var gate = _instanceGates.GetOrAdd(
            instanceId,
            static _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    internal async ValueTask<RuntimeInstanceAdvanceResult> AdvanceInstanceAsync(
        FlowInstanceId instanceId,
        int maxSteps,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithInstanceGateAsync(
                instanceId,
                gateCancellationToken => AdvanceInstanceCoreAsync(
                    instanceId,
                    maxSteps,
                    gateCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<RuntimeInstanceAdvanceResult> AdvanceInstanceCoreAsync(
        FlowInstanceId instanceId,
        int maxSteps,
        CancellationToken cancellationToken)
    {
        var before = await _store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        if (before is null || IsTerminal(before.Status))
        {
            return RuntimeInstanceAdvanceResult.Empty;
        }

        var session = new FlowExecutionSession(instanceId);
        var beforeSteps = await _store.Steps
            .GetByFlowInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        await _flowExecutor.ExecuteAsync(instanceId, session, cancellationToken)
            .ConfigureAwait(false);

        var afterReplay = await _store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var afterReplaySteps = await _store.Steps
            .GetByFlowInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        if (afterReplay is null || IsTerminal(afterReplay.Status))
        {
            return new RuntimeInstanceAdvanceResult(
                ReplayedFlows: 1,
                ExecutedSteps: 0,
                CompletedFlows: afterReplay?.Status == FlowInstanceStatus.Completed ? 1 : 0,
                FailedFlows: afterReplay?.Status == FlowInstanceStatus.Failed ? 1 : 0);
        }

        var executed = await _stepExecutor
            .ExecuteReadyStepsAsync(session, maxSteps, cancellationToken)
            .ConfigureAwait(false);

        if (executed > 0)
        {
            await _flowExecutor.ExecuteAsync(instanceId, session, cancellationToken)
                .ConfigureAwait(false);
        }

        var afterSteps = await _store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        var replayMadeProgress =
            before.Status != afterReplay?.Status ||
            StepsChanged(beforeSteps, afterReplaySteps);

        return new RuntimeInstanceAdvanceResult(
            ReplayedFlows: replayMadeProgress ? 1 : 0,
            executed,
            CompletedFlows: before.Status != FlowInstanceStatus.Completed &&
                afterSteps?.Status == FlowInstanceStatus.Completed ? 1 : 0,
            FailedFlows: before.Status != FlowInstanceStatus.Failed &&
                afterSteps?.Status == FlowInstanceStatus.Failed ? 1 : 0);
    }

    public ValueTask SignalAsync<TSignal>(
        FlowInstanceId instanceId,
        SignalName signalName,
        TSignal payload,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Signal delivery is not supported by the local MVP runtime yet.");
    }

    public ValueTask SignalAsync<TSignal>(
        SignalName signalName,
        CorrelationKey correlationKey,
        TSignal payload,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Signal delivery is not supported by the local MVP runtime yet.");
    }

    public async ValueTask CancelAsync(
        FlowInstanceId instanceId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await _store
            .ExecuteAsync(
                (storeSession, storeCancellationToken) =>
                    storeSession.FlowInstances.UpdateStatusAsync(
                        instanceId,
                        FlowInstanceStatus.Cancelled,
                        _timeProvider.GetUtcNow(),
                        storeCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask RetryAsync(
        FlowInstanceId instanceId,
        StepId? stepId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Retry is not supported by the local MVP runtime yet.");
    }

    public async ValueTask<FlowInstanceSnapshot?> GetInstanceAsync(
        FlowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        if (instance is null)
        {
            return null;
        }

        var steps = await _store.Steps
            .GetByFlowInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        return new FlowInstanceSnapshot
        {
            InstanceId = instance.InstanceId,
            FlowName = instance.FlowName,
            FlowVersion = instance.FlowVersion,
            Status = instance.Status,
            CreatedAt = instance.CreatedAt,
            CompletedAt = instance.CompletedAt,
            Steps = steps.Select(step => new StepSnapshot
            {
                StepId = step.StepId,
                Name = step.Name,
                Kind = step.Kind,
                Status = step.Status,
                HandlerId = step.HandlerId,
                Queue = step.Queue,
                Attempt = step.Attempt,
                StartedAt = step.StartedAt,
                CompletedAt = step.CompletedAt,
                LastError = step.Error
            }).ToArray()
        };
    }

    private static bool IsTerminal(FlowInstanceStatus status)
    {
        return status is FlowInstanceStatus.Completed
            or FlowInstanceStatus.Failed
            or FlowInstanceStatus.Cancelled
            or FlowInstanceStatus.TimedOut;
    }

    private static bool StepsChanged(
        IReadOnlyList<StepInstanceRecord> before,
        IReadOnlyList<StepInstanceRecord> after)
    {
        if (before.Count != after.Count)
        {
            return true;
        }

        for (var i = 0; i < before.Count; i++)
        {
            if (before[i].StepId != after[i].StepId ||
                before[i].Status != after[i].Status ||
                before[i].Attempt != after[i].Attempt ||
                before[i].CompletedAt != after[i].CompletedAt ||
                before[i].Error != after[i].Error ||
                !PayloadEquals(before[i].Result, after[i].Result))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PayloadEquals(
        SerializedPayload? left,
        SerializedPayload? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.ContentType == right.ContentType &&
            left.TypeName == right.TypeName &&
            left.Data.SequenceEqual(right.Data);
    }
}

internal readonly record struct RuntimeInstanceAdvanceResult(
    int ReplayedFlows,
    int ExecutedSteps,
    int CompletedFlows,
    int FailedFlows)
{
    public static RuntimeInstanceAdvanceResult Empty { get; } = new(
        ReplayedFlows: 0,
        ExecutedSteps: 0,
        CompletedFlows: 0,
        FailedFlows: 0);
}

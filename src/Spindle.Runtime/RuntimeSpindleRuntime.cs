using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Exceptions;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.Nodes;
using Spindle.Persistence.Timers;
using Spindle.Runtime;

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
    private readonly FlowExecutor _flowExecutor;
    private readonly StepExecutor _stepExecutor;
    private readonly ConcurrentDictionary<FlowInstanceId, SemaphoreSlim> _instanceGates = new();

    internal FlowRegistry Registry => _registry;

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

        _flowExecutor = new FlowExecutor(
            _store,
            _registry,
            _serializer,
            _timeProvider,
            new BarrierProcessor(_store, _serializer, _timeProvider),
            _stepHandlers,
            _services);
        _stepExecutor = new StepExecutor(
            _store,
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

    public ValueTask<FlowInstanceHandle<TResult>> EnqueueAsync<TRequest, TResult>(
        FlowName flowName,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var version = options?.Version;
        return EnqueueAsync<TRequest, TResult>(
            flowName,
            version ?? _registry.Resolve(flowName).FlowVersion,
            request,
            options,
            cancellationToken);
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

    private async ValueTask<(FlowInstanceHandle<TResult> handle, bool created)> InternalQueueAsync<TRequest, TResult>(
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

        return (handle, created);
    }

    public async ValueTask<FlowInstanceHandle<TResult>> EnqueueAsync<TRequest, TResult>(
        FlowName flowName,
        FlowVersion flowVersion,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (handle, _) = await InternalQueueAsync<TRequest, TResult>(flowName, flowVersion, request, options, cancellationToken);

        return handle;
    }

    public async ValueTask<FlowInstanceHandle<TResult>> StartAsync<TRequest, TResult>(
        FlowName flowName,
        FlowVersion flowVersion,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (handle, created) = await InternalQueueAsync<TRequest, TResult>(flowName, flowVersion, request, options, cancellationToken);

        if (created)
        {
            var instanceId = handle.InstanceId;
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
        var iterations = 0;

        while (true)
        {
            // Each iteration takes the instance gate separately, so recovery and hosted advancement can
            // interleave while this call waits for a pending retry.
            var iteration = await ExecuteWithInstanceGateAsync(
                    handle.InstanceId,
                    gateCancellationToken => RunIterationAsync<TResult>(
                        handle.InstanceId,
                        session,
                        gateCancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            if (iteration.Completed)
            {
                return iteration.Result!;
            }

            if (iteration.MadeProgress)
            {
                if (++iterations >= _options.MaxRunIterations)
                {
                    throw new InvalidOperationException(
                        $"Flow '{flowName}' did not complete within {_options.MaxRunIterations} local runtime iterations.");
                }

                continue;
            }

            if (iteration.NextRetryAt is not { } retryAt)
            {
                throw new NotSupportedException(
                    $"Flow '{flowName}' instance '{handle.InstanceId}' is waiting, but no local ready steps can be executed by this runtime.");
            }

            // Waiting for a retry is not an iteration: no work could be done until the retry is due.
            await _timeProvider.DelayUntilAsync(retryAt, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<RunIteration<TResult>> RunIterationAsync<TResult>(
        FlowInstanceId instanceId,
        FlowExecutionSession session,
        CancellationToken cancellationToken)
    {
        var firedTimers = await FireDueTimersAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        await _flowExecutor
            .ExecuteAsync(instanceId, session, cancellationToken)
            .ConfigureAwait(false);

        var instance = await _store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Flow instance '{instanceId}' disappeared.");

        switch (instance.Status)
        {
            case FlowInstanceStatus.Completed:
                return RunIteration<TResult>.Complete(
                    instance.Result is null
                        ? default!
                        : _serializer.Deserialize<TResult>(instance.Result));
            case FlowInstanceStatus.Failed:
                throw new InvalidOperationException(
                    $"Flow '{instance.FlowName}' instance '{instance.InstanceId}' failed: {instance.Error}");
            case FlowInstanceStatus.Cancelled:
                throw new OperationCanceledException(
                    $"Flow '{instance.FlowName}' instance '{instance.InstanceId}' was cancelled.");
        }

        var executed = await _stepExecutor
            .ExecuteReadyStepsAsync(session, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        firedTimers += await FireDueTimersAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        if (executed > 0 || firedTimers > 0)
        {
            return RunIteration<TResult>.Progress;
        }

        var nodes = await _store.Nodes
            .GetByFlowInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        return RunIteration<TResult>.Idle(nodes
            .Where(node => node.Status == NodeStatus.Waiting)
            .Min(node => node.RetryAt));
    }

    private readonly record struct RunIteration<TResult>(
        bool Completed,
        TResult? Result,
        bool MadeProgress,
        DateTimeOffset? NextRetryAt)
    {
        public static RunIteration<TResult> Progress { get; } = new(false, default, MadeProgress: true, null);

        public static RunIteration<TResult> Complete(TResult result) => new(true, result, MadeProgress: true, null);

        public static RunIteration<TResult> Idle(DateTimeOffset? nextRetryAt) => new(false, default, MadeProgress: false, nextRetryAt);
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

                        var flow = await storeSession.FlowInstances
                            .GetAsync(timer.FlowInstanceId, storeCancellationToken)
                            .ConfigureAwait(false);

                        var step = await storeSession.Nodes
                            .GetAsync(timer.FlowInstanceId, timer.NodeId, storeCancellationToken)
                            .ConfigureAwait(false);

                        if (flow is null ||
                            IsTerminal(flow.Status) ||
                            step is null ||
                            step.Status is NodeStatus.Completed
                                or NodeStatus.Failed
                                or NodeStatus.Cancelled
                                or NodeStatus.TimedOut
                                or NodeStatus.Skipped)
                        {
                            await storeSession.Timers
                                .MarkFiredAsync(timer.FlowInstanceId, timer.NodeId, now, storeCancellationToken)
                                .ConfigureAwait(false);
                            continue;
                        }

                        if (step.Kind == NodeKind.Step)
                        {
                            // A step timer is always a retry. It only makes the step ready again; a timer whose due
                            // time no longer matches the step's pending retry is stale and is discarded.
                            if (await storeSession.Nodes
                                    .TryMarkRetryReadyAsync(timer.FlowInstanceId, timer.NodeId, timer.DueAt, now, storeCancellationToken)
                                    .ConfigureAwait(false))
                            {
                                fired++;
                            }

                            await storeSession.Timers
                                .MarkFiredAsync(timer.FlowInstanceId, timer.NodeId, now, storeCancellationToken)
                                .ConfigureAwait(false);
                            continue;
                        }

                        if (step.Kind == NodeKind.ConditionWait)
                        {
                            var condition = await storeSession.Conditions
                                .GetAsync(timer.FlowInstanceId, timer.NodeId, storeCancellationToken)
                                .ConfigureAwait(false)
                                ?? throw new InvalidOperationException(
                                    $"Condition metadata for node '{timer.NodeId}' does not exist.");

                            if (condition.ExpiresAt is { } expiresAt && now >= expiresAt)
                            {
                                await storeSession.Nodes.MarkTimedOutAsync(
                                        timer.FlowInstanceId,
                                        timer.NodeId,
                                        step.Attempt,
                                        $"Condition node '{timer.NodeId}' timed out at {expiresAt:O}.",
                                        now,
                                        storeCancellationToken)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                await storeSession.Nodes.MarkReadyAsync(
                                        timer.FlowInstanceId,
                                        timer.NodeId,
                                        now,
                                        storeCancellationToken)
                                    .ConfigureAwait(false);
                            }

                            await storeSession.Timers.MarkFiredAsync(
                                    timer.FlowInstanceId,
                                    timer.NodeId,
                                    now,
                                    storeCancellationToken)
                                .ConfigureAwait(false);
                            fired++;
                            continue;
                        }

                        await storeSession.Nodes
                            .MarkCompletedAsync(timer.FlowInstanceId, timer.NodeId, -1, result: null, now, storeCancellationToken)
                            .ConfigureAwait(false);

                        await storeSession.Timers
                            .MarkFiredAsync(timer.FlowInstanceId, timer.NodeId, now, storeCancellationToken)
                            .ConfigureAwait(false);

                        await storeSession.Nodes
                            .MarkDependentsReadyAsync(timer.FlowInstanceId, [timer.NodeId], _timeProvider.GetUtcNow(), storeCancellationToken)
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
        using var activity = Telemetry.ActivitySource.StartActivity();
        activity?.SetTag("spindle.instance-id", instanceId.Value);
        activity?.SetTag("spindle.worker-id", _options.WorkerId);
        var before = await _store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        if (before is null || IsTerminal(before.Status))
        {
            return RuntimeInstanceAdvanceResult.Empty;
        }

        var session = new FlowExecutionSession(instanceId);
        var beforeNodes = await _store.Nodes
            .GetByFlowInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        await _flowExecutor.ExecuteAsync(instanceId, session, cancellationToken)
            .ConfigureAwait(false);

        var afterReplay = await _store.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var afterReplayNodes = await _store.Nodes
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
            NodesChanged(beforeNodes, afterReplayNodes);

        return new RuntimeInstanceAdvanceResult(
            ReplayedFlows: replayMadeProgress ? 1 : 0,
            executed,
            CompletedFlows: before.Status != FlowInstanceStatus.Completed &&
                afterSteps?.Status == FlowInstanceStatus.Completed ? 1 : 0,
            FailedFlows: before.Status != FlowInstanceStatus.Failed &&
                afterSteps?.Status == FlowInstanceStatus.Failed ? 1 : 0);
    }

    private async ValueTask InternalSignalAsync<TSignal>(
        FlowInstanceId? instanceId,
        SignalName signalName,
        CorrelationKey correlationKey,
        TSignal? payload,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();
        await _store
            .ExecuteAsync(
                async (storeSession, storeCancellationToken) =>
                {
                    var pl = payload != null ? _serializer.Serialize(payload) : null;
                    // Start by logging it
                    await storeSession.Signals.AppendSignalAsync(new Persistence.Signals.SignalRecord
                    {
                        FlowInstanceId = instanceId,
                        SignalName = signalName,
                        CorrelationKey = correlationKey,
                        Payload = pl,
                        RaisedAt = DateTimeOffset.Now
                    }, storeCancellationToken);

                    var open = await storeSession.Signals.GetOpenWaitsAsync(signalName, correlationKey, storeCancellationToken);
                    // If instance is specified, then filter
                    if (instanceId.HasValue)
                        open = [.. open.Where(x => x.FlowInstanceId == instanceId)];

                    foreach (var wait in open)
                    {
                        var step = await storeSession.Nodes.GetAsync(wait.FlowInstanceId, wait.NodeId, storeCancellationToken);
                        // Don't set it to completed if the step is not waiting
                        // If the step is not waiting that means that the step proabaly is not expecting a signal
                        if (step is { Status: NodeStatus.Waiting })
                        {
                            await storeSession.Nodes.MarkCompletedAsync(
                                wait.FlowInstanceId,
                                wait.NodeId,
                                step.Attempt,
                                pl,
                                now,
                                storeCancellationToken);

                            await storeSession.Nodes.MarkDependentsReadyAsync(
                                wait.FlowInstanceId,
                                [wait.NodeId],
                                now,
                                storeCancellationToken);
                        }

                        // Mark the wait as completed to clear it from the next run
                        await storeSession.Signals.MarkWaitCompletedAsync(wait.FlowInstanceId, wait.NodeId, now, storeCancellationToken);
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask SignalAsync<TSignal>(
        FlowInstanceId instanceId,
        SignalName signalName,
        CorrelationKey correlationKey,
        TSignal payload,
        CancellationToken cancellationToken = default)
        => await InternalSignalAsync<TSignal>(instanceId, signalName, correlationKey, payload, cancellationToken);

    public async ValueTask SignalAsync<TSignal>(
        SignalName signalName,
        CorrelationKey correlationKey,
        TSignal payload,
        CancellationToken cancellationToken = default)
        => await InternalSignalAsync<TSignal>(null, signalName, correlationKey, payload, cancellationToken);

    public async ValueTask SignalAsync(
        FlowInstanceId instanceId,
        SignalName signalName,
        CorrelationKey correlationKey,
        CancellationToken cancellationToken = default)
        => await InternalSignalAsync<Unit?>(instanceId, signalName, correlationKey, null, cancellationToken);

    public async ValueTask SignalAsync(
        SignalName signalName,
        CorrelationKey correlationKey,
        CancellationToken cancellationToken = default)
        => await InternalSignalAsync<Unit?>(null, signalName, correlationKey, null, cancellationToken);

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

    public async ValueTask RetryAsync(
        FlowInstanceId instanceId,
        NodeId? nodeId = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithInstanceGateAsync(
                instanceId,
                gateCancellationToken => _store.ExecuteAsync(
                    (storeSession, storeCancellationToken) => ScheduleRecoveryAsync(
                        storeSession,
                        instanceId,
                        nodeId,
                        storeCancellationToken),
                    gateCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask ScheduleRecoveryAsync(
        ISpindleStoreSession storeSession,
        FlowInstanceId instanceId,
        NodeId? nodeId,
        CancellationToken cancellationToken)
    {
        var instance = await storeSession.FlowInstances
            .GetAsync(instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Flow instance '{instanceId}' does not exist.");

        if (instance.Status is FlowInstanceStatus.Completed or FlowInstanceStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"Flow instance '{instanceId}' is {instance.Status.ToString().ToLowerInvariant()} and cannot be retried.");
        }

        var nodes = await storeSession.Nodes
            .GetByFlowInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var targets = SelectRecoveryTargets(instanceId, nodes, nodeId);
        var now = _timeProvider.GetUtcNow();

        // Each transition is conditional on the state read above, so a concurrent recovery or a worker that
        // already moved the step on makes the transition a no-op instead of scheduling it twice.
        var recovered = new HashSet<NodeId>();
        foreach (var target in targets)
        {
            if (!await storeSession.Nodes
                    .TryScheduleRecoveryAsync(instanceId, target.NodeId, target.Status, target.Attempt, now, now, cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            await storeSession.Timers
                .CreateAsync(
                    new TimerRecord
                    {
                        FlowInstanceId = instanceId,
                        NodeId = target.NodeId,
                        DueAt = now,
                        CreatedAt = now
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            recovered.Add(target.NodeId);
        }

        if (recovered.Count == 0)
        {
            throw new InvalidOperationException(
                $"Flow instance '{instanceId}' has no failed or timed-out steps left to retry.");
        }

        await ReopenFailedBarriersAsync(storeSession, instanceId, nodes, recovered, now, cancellationToken)
            .ConfigureAwait(false);

        await storeSession.FlowInstances
            .TryReopenAsync(instanceId, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<NodeInstanceRecord> SelectRecoveryTargets(
        FlowInstanceId instanceId,
        IReadOnlyList<NodeInstanceRecord> nodes,
        NodeId? nodeId)
    {
        if (nodeId is not { } targetId)
        {
            var targets = nodes.Where(IsRecoverable).ToArray();
            return targets.Length > 0
                ? targets
                : throw new InvalidOperationException(
                    $"Flow instance '{instanceId}' has no failed or timed-out steps to retry.");
        }

        var target = nodes.FirstOrDefault(node => node.NodeId == targetId)
            ?? throw new InvalidOperationException(
                $"Node '{targetId}' does not exist for flow instance '{instanceId}'.");

        return IsRecoverable(target)
            ? [target]
            : throw new InvalidOperationException(
                $"Node '{targetId}' is a {target.Kind} node with status {target.Status}; only failed or timed-out steps can be retried.");
    }

    private static bool IsRecoverable(NodeInstanceRecord node)
        => node.Kind == NodeKind.Step && node.Status is NodeStatus.Failed or NodeStatus.TimedOut;

    /// <summary>
    /// Reopens failed wait barriers that depend, directly or through other failed barriers, on a recovered
    /// step, so replay evaluates them again. Completed barriers keep their result.
    /// </summary>
    private static async ValueTask ReopenFailedBarriersAsync(
        ISpindleStoreSession storeSession,
        FlowInstanceId instanceId,
        IReadOnlyList<NodeInstanceRecord> nodes,
        HashSet<NodeId> recovered,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reopened = new HashSet<NodeId>(recovered);
        var failedBarriers = nodes
            .Where(node => node.Kind is NodeKind.WaitAll or NodeKind.WaitAny && node.Status == NodeStatus.Failed)
            .ToList();

        bool changed;
        do
        {
            changed = false;
            foreach (var barrier in failedBarriers.Where(barrier => barrier.Dependencies.Any(reopened.Contains)).ToArray())
            {
                failedBarriers.Remove(barrier);
                if (await storeSession.Nodes
                        .TryReopenBarrierAsync(instanceId, barrier.NodeId, now, cancellationToken)
                        .ConfigureAwait(false))
                {
                    reopened.Add(barrier.NodeId);
                    changed = true;
                }
            }
        }
        while (changed);
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

        var nodes = await _store.Nodes
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
            Nodes = nodes.Select(node => new NodeSnapshot
            {
                NodeId = node.NodeId,
                Name = node.Name,
                Kind = node.Kind,
                Status = node.Status,
                HandlerId = node.HandlerId,
                Queue = node.Queue,
                Attempt = node.Attempt,
                RetryAt = node.RetryAt,
                StartedAt = node.StartedAt,
                CompletedAt = node.CompletedAt,
                LastError = node.Error
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

    private static bool NodesChanged(
        IReadOnlyList<NodeInstanceRecord> before,
        IReadOnlyList<NodeInstanceRecord> after)
    {
        if (before.Count != after.Count)
        {
            return true;
        }

        for (var i = 0; i < before.Count; i++)
        {
            if (before[i].NodeId != after[i].NodeId ||
                before[i].Status != after[i].Status ||
                before[i].Attempt != after[i].Attempt ||
                before[i].RetryAt != after[i].RetryAt ||
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

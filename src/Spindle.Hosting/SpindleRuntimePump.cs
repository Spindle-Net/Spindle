using Spindle.Abstractions.Core;
using Spindle.Persistence;
using Microsoft.Extensions.Options;

namespace Spindle.Hosting;

public sealed class SpindleRuntimePump(
    RuntimeSpindleRuntime runtime,
    ISpindleStore store,
    IOptions<SpindleHostOptions> options)
    : ISpindleRuntimePump
{
    private readonly Lock _gate = new();
    private readonly Dictionary<FlowInstanceId, Task<RuntimeInstanceAdvanceResult>> _inFlight = [];
    private readonly HashSet<FlowInstanceId> _quiescent = [];
    private readonly SemaphoreSlim _wakeups = new(0);
    private readonly SpindleHostOptions _options = options.Value;

    public async ValueTask<SpindlePumpResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var completed = await DrainCompletedAsync().ConfigureAwait(false);

        var firedTimers = await runtime
            .FireDueTimersAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (firedTimers > 0)
        {
            lock (_gate)
            {
                _quiescent.Clear();
            }
        }

        var scheduled = 0;
        var runnable = await store.FlowInstances
            .GetRunnableAsync(_options.MaxFlowInstancesPerTick, cancellationToken)
            .ConfigureAwait(false);

        foreach (var instance in runnable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsQuiescent(instance.InstanceId))
            {
                continue;
            }

            if (!TrySchedule(instance.InstanceId, cancellationToken))
            {
                continue;
            }

            scheduled++;
        }

        return completed.Add(new SpindlePumpResult(
            ScheduledFlows: scheduled,
            InFlightFlows: InFlightCount,
            ReplayedFlows: 0,
            ExecutedSteps: 0,
            FiredTimers: firedTimers,
            CompletedFlows: 0,
            FailedFlows: 0));
    }

    public async ValueTask<SpindlePumpResult> RunUntilIdleAsync(
        int maxIterations = 100,
        CancellationToken cancellationToken = default)
    {
        var total = SpindlePumpResult.Empty;

        for (var i = 0; i < maxIterations; i++)
        {
            var result = await RunOnceAsync(cancellationToken)
                .ConfigureAwait(false);
            total = total.Add(result);

            if (result is { HasProgress: false, InFlightFlows: 0 })
            {
                return total;
            }

            if (result is { InFlightFlows: > 0, HasProgress: false })
            {
                await WaitForWakeupAsync(_options.PollInterval, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return total;
    }

    public async ValueTask<bool> WaitForWakeupAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return _wakeups.Wait(0, cancellationToken);
        }

        return await _wakeups
            .WaitAsync(timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    private int InFlightCount
    {
        get
        {
            lock (_gate)
            {
                return _inFlight.Count;
            }
        }
    }

    private bool TrySchedule(
        FlowInstanceId instanceId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_inFlight.ContainsKey(instanceId) ||
                _inFlight.Count >= _options.MaxConcurrentFlowInstances)
            {
                return false;
            }

            var task = Task.Run(
                () => runtime
                    .AdvanceInstanceAsync(
                        instanceId,
                        _options.MaxStepsPerFlowPerTick,
                        cancellationToken)
                    .AsTask(),
                CancellationToken.None);
            task.ContinueWith(
                _ => SignalWakeup(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            _inFlight[instanceId] = task;

            return true;
        }
    }

    private void SignalWakeup()
    {
        try
        {
            _wakeups.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool IsQuiescent(
        FlowInstanceId instanceId)
    {
        lock (_gate)
        {
            return _quiescent.Contains(instanceId);
        }
    }

    private async ValueTask<SpindlePumpResult> DrainCompletedAsync()
    {
        KeyValuePair<FlowInstanceId, Task<RuntimeInstanceAdvanceResult>>[] completed;

        lock (_gate)
        {
            completed = _inFlight
                .Where(pair => pair.Value.IsCompleted)
                .ToArray();

            foreach (var item in completed)
            {
                _inFlight.Remove(item.Key);
            }
        }

        var result = SpindlePumpResult.Empty;

        foreach (var (instanceId, task) in completed)
        {
            RuntimeInstanceAdvanceResult completedResult;

            try
            {
                completedResult = await task.ConfigureAwait(false);
            }
            catch
            {
                lock (_gate)
                {
                    _quiescent.Remove(instanceId);
                }

                result = result.Add(new SpindlePumpResult(
                    ScheduledFlows: 0,
                    InFlightFlows: InFlightCount,
                    ReplayedFlows: 0,
                    ExecutedSteps: 0,
                    FiredTimers: 0,
                    CompletedFlows: 0,
                    FailedFlows: 1));
                continue;
            }

            lock (_gate)
            {
                if (completedResult is { ReplayedFlows: 0, ExecutedSteps: 0, CompletedFlows: 0, FailedFlows: 0 })
                {
                    _quiescent.Add(instanceId);
                }
                else
                {
                    _quiescent.Remove(instanceId);
                }
            }

            result = result.Add(new SpindlePumpResult(
                ScheduledFlows: 0,
                InFlightFlows: InFlightCount,
                ReplayedFlows: completedResult.ReplayedFlows,
                ExecutedSteps: completedResult.ExecutedSteps,
                FiredTimers: 0,
                CompletedFlows: completedResult.CompletedFlows,
                FailedFlows: completedResult.FailedFlows));
        }

        return result;
    }
}

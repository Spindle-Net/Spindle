using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Hosting;
using Spindle.Persistence.InMemory;
using Microsoft.Extensions.Options;

namespace Spindle.Testing;

public sealed class SpindleTestHarness
{
    private readonly StepHandlerRegistry _stepHandlers = new();
    private readonly MutableServiceProvider _services = new();

    public SpindleTestHarness(
        DateTimeOffset? initialUtcNow = null,
        SpindleHostOptions? hostOptions = null)
    {
        Clock = new FakeSpindleClock(initialUtcNow);
        Store = new InMemorySpindleStore();
        Serializer = new JsonSpindleSerializer();

        var options = hostOptions ?? new SpindleHostOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            MaxConcurrentFlowInstances = 4,
            MaxFlowInstancesPerTick = 100,
            MaxStepsPerFlowPerTick = 1,
            WorkerId = "test-worker"
        };

        Runtime = new RuntimeSpindleRuntime(
            Store,
            options: new RuntimeSpindleOptions
            {
                Serializer = Serializer,
                TimeProvider = Clock,
                Services = _services,
                StepHandlers = _stepHandlers,
                WorkerId = options.WorkerId,
                StepLeaseDuration = options.LeaseDuration
            });

        Pump = new SpindleRuntimePump(
            Runtime,
            Store,
            Options.Create(options));
    }

    public FakeSpindleClock Clock { get; }

    public InMemorySpindleStore Store { get; }

    public JsonSpindleSerializer Serializer { get; }

    public RuntimeSpindleRuntime Runtime { get; }

    public ISpindleRuntimePump Pump { get; }

    public RuntimeSpindleRuntime RegisterFlow<TRequest, TResult>(
        FlowName flowName,
        ISpindleFlow<TRequest, TResult> flow,
        FlowVersion? flowVersion = null)
    {
        return Runtime.RegisterFlow(flowName, flow, flowVersion);
    }

    public RuntimeSpindleRuntime RegisterFlow<TRequest, TResult>(
        FlowName flowName,
        Func<IFlowContext, TRequest, ValueTask<TResult>> run,
        FlowVersion? flowVersion = null)
    {
        return Runtime.RegisterFlow(flowName, run, flowVersion);
    }

    public void RegisterStepHandler<THandler, TRequest, TResult>(
        StepHandlerId handlerId,
        THandler handler)
        where THandler : class, IStepHandler<TRequest, TResult>
    {
        _stepHandlers.Register<THandler, TRequest, TResult>(handlerId);
        _services.Set(typeof(THandler), handler);
    }

    public ValueTask<FlowInstanceHandle<TResult>> StartFlowAsync<TRequest, TResult>(
        FlowName flowName,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Runtime.StartAsync<TRequest, TResult>(
            flowName,
            request,
            options,
            cancellationToken);
    }

    public ValueTask<SpindlePumpResult> PumpOnceAsync(
        CancellationToken cancellationToken = default)
    {
        return Pump.RunOnceAsync(cancellationToken);
    }

    public ValueTask<SpindlePumpResult> PumpUntilIdleAsync(
        int maxIterations = 100,
        CancellationToken cancellationToken = default)
    {
        return Pump.RunUntilIdleAsync(maxIterations, cancellationToken);
    }

    public async ValueTask<FlowInstanceSnapshot> PumpUntilCompletedAsync(
        FlowInstanceId instanceId,
        int maxIterations = 100,
        CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < maxIterations; i++)
        {
            var snapshot = await Runtime
                .GetInstanceAsync(instanceId, cancellationToken)
                .ConfigureAwait(false);

            if (snapshot?.Status == FlowInstanceStatus.Completed)
            {
                return snapshot;
            }

            if (snapshot?.Status is FlowInstanceStatus.Failed or FlowInstanceStatus.Cancelled or FlowInstanceStatus.TimedOut)
            {
                throw new InvalidOperationException(
                    $"Flow instance '{instanceId}' reached terminal status '{snapshot.Status}'.");
            }

            await PumpOnceAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Flow instance '{instanceId}' did not complete within {maxIterations} pump iterations.");
    }

    public DateTimeOffset AdvanceTimeBy(
        TimeSpan duration)
    {
        return Clock.AdvanceBy(duration);
    }

    public DateTimeOffset AdvanceTimeTo(
        DateTimeOffset utcNow)
    {
        return Clock.AdvanceTo(utcNow);
    }

    public ValueTask<FlowInstanceSnapshot?> GetSnapshotAsync(
        FlowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        return Runtime.GetInstanceAsync(instanceId, cancellationToken);
    }

    private sealed class MutableServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = [];

        public object? GetService(
            Type serviceType)
        {
            return _services.TryGetValue(serviceType, out var service)
                ? service
                : null;
        }

        public void Set(
            Type serviceType,
            object service)
        {
            _services[serviceType] = service;
        }
    }
}

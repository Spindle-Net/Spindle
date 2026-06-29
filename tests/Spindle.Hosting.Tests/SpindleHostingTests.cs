using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Hosting;
using Spindle.Persistence;
using Spindle.Persistence.InMemory;
using Spindle.Testing;
using Xunit;

namespace Spindle.Hosting.Tests;

public sealed class SpindleHostingTests
{
    [Fact]
    public async Task Pump_CompletesStartedFlow()
    {
        var harness = new SpindleTestHarness();
        var flowName = new FlowName("pump-completes-started-flow");

        harness.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, request) =>
            {
                var value = context.Step<int>(
                    "add-one",
                    "Add one",
                    () => ValueTask.FromResult(request.Value + 1));

                return new TestResult(await value);
            });

        var handle = await harness.StartFlowAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(41));

        var snapshot = await harness.PumpUntilCompletedAsync(handle.InstanceId);
        var instance = await harness.Store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(FlowInstanceStatus.Completed, snapshot.Status);
        Assert.NotNull(instance?.Result);
        Assert.Equal(new TestResult(42), harness.Serializer.Deserialize<TestResult>(instance.Result));
    }

    [Fact]
    public async Task Pump_FiresDueTimerAndCompletesFlow()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T10:00:00Z");
        var harness = new SpindleTestHarness(initial);
        var flowName = new FlowName("pump-fires-due-timer");

        harness.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.Delay("delay", TimeSpan.FromMinutes(5));
                return new TestResult(42);
            });

        var handle = await harness.StartFlowAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        var idle = await harness.PumpUntilIdleAsync(maxIterations: 5);
        var waiting = await harness.GetSnapshotAsync(handle.InstanceId);

        Assert.False(idle.HasProgress);
        Assert.Equal(FlowInstanceStatus.Waiting, waiting?.Status);

        harness.AdvanceTimeBy(TimeSpan.FromMinutes(5));

        var completed = await harness.PumpUntilCompletedAsync(handle.InstanceId);

        Assert.Equal(FlowInstanceStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task StepHandler_IsResolvedByHandlerId()
    {
        var harness = new SpindleTestHarness();
        var flowName = new FlowName("handler-resolved-by-id");
        var handlerId = new StepHandlerId("double");

        harness.RegisterStepHandler<DoubleHandler, HandlerRequest, int>(
            handlerId,
            new DoubleHandler());

        harness.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, request) =>
            {
                var value = context.StepHandler<HandlerRequest, int>(
                    "double",
                    "Double",
                    handlerId,
                    [],
                    _ => new HandlerRequest(request.Value));

                return new TestResult(await value);
            });

        var handle = await harness.StartFlowAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(21));

        var snapshot = await harness.PumpUntilCompletedAsync(handle.InstanceId);
        var instance = await harness.Store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(FlowInstanceStatus.Completed, snapshot.Status);
        Assert.NotNull(instance?.Result);
        Assert.Equal(new TestResult(42), harness.Serializer.Deserialize<TestResult>(instance.Result));
    }

    [Fact]
    public async Task LongRunningStep_DoesNotBlockUnrelatedFlow()
    {
        var gate = new GateHandler();
        var harness = new SpindleTestHarness(
            hostOptions: new SpindleHostOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(10),
                MaxConcurrentFlowInstances = 2,
                MaxFlowInstancesPerTick = 10,
                MaxStepsPerFlowPerTick = 1,
                WorkerId = "test-worker"
            });

        var blockingFlow = new FlowName("blocking-flow");
        var simpleFlow = new FlowName("simple-flow");
        var handlerId = new StepHandlerId("gate");

        harness.RegisterStepHandler<GateHandler, HandlerRequest, int>(handlerId, gate);
        harness.RegisterFlow<TestRequest, TestResult>(
            blockingFlow,
            async (context, request) =>
            {
                var value = context.StepHandler<HandlerRequest, int>(
                    "gate",
                    "Gate",
                    handlerId,
                    [],
                    _ => new HandlerRequest(request.Value));

                return new TestResult(await value);
            });
        harness.RegisterFlow<TestRequest, TestResult>(
            simpleFlow,
            async (context, request) =>
            {
                var value = context.Step<int>(
                    "add-one",
                    "Add one",
                    () => ValueTask.FromResult(request.Value + 1));

                return new TestResult(await value);
            });

        var blocked = await harness.StartFlowAsync<TestRequest, TestResult>(
            blockingFlow,
            new TestRequest(40));

        await harness.PumpOnceAsync();
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var simple = await harness.StartFlowAsync<TestRequest, TestResult>(
            simpleFlow,
            new TestRequest(41));

        var simpleSnapshot = await harness.PumpUntilCompletedAsync(simple.InstanceId);
        var blockedSnapshot = await harness.GetSnapshotAsync(blocked.InstanceId);

        Assert.Equal(FlowInstanceStatus.Completed, simpleSnapshot.Status);
        Assert.NotEqual(FlowInstanceStatus.Completed, blockedSnapshot?.Status);

        gate.Release.SetResult();

        var completedBlocked = await harness.PumpUntilCompletedAsync(blocked.InstanceId);
        var blockedInstance = await harness.Store.FlowInstances.GetAsync(blocked.InstanceId);

        Assert.Equal(FlowInstanceStatus.Completed, completedBlocked.Status);
        Assert.NotNull(blockedInstance?.Result);
        Assert.Equal(new TestResult(40), harness.Serializer.Deserialize<TestResult>(blockedInstance.Result));
    }

    [Fact]
    public async Task Pump_DoesNotRaceWithInitialFlowExpansion()
    {
        var declaredStep = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialReplay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var harness = new SpindleTestHarness(
            hostOptions: new SpindleHostOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(10),
                MaxConcurrentFlowInstances = 2,
                MaxFlowInstancesPerTick = 10,
                MaxStepsPerFlowPerTick = 1,
                WorkerId = "test-worker"
            });
        var flowName = new FlowName("pump-initial-expansion-race");

        harness.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, request) =>
            {
                var invocation = Interlocked.Increment(ref invocationCount);
                var value = context.Step<int>(
                    "upper",
                    "Upper",
                    () => ValueTask.FromResult(request.Value + 1));

                if (invocation == 1)
                {
                    declaredStep.TrySetResult();
                    await releaseInitialReplay.Task
                        .WaitAsync(context.CancellationToken)
                        .ConfigureAwait(false);
                }

                return new TestResult(await value);
            });

        var startTask = harness
            .StartFlowAsync<TestRequest, TestResult>(
                flowName,
                new TestRequest(41))
            .AsTask();

        await declaredStep.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var runnable = await harness.Store.FlowInstances.GetRunnableAsync(maxCount: 10);
        var instance = Assert.Single(runnable);

        var scheduled = await harness.PumpOnceAsync();
        Assert.Equal(1, scheduled.ScheduledFlows);

        releaseInitialReplay.SetResult();

        var handle = await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(instance.InstanceId, handle.InstanceId);

        var snapshot = await harness.PumpUntilCompletedAsync(handle.InstanceId);
        var completed = await harness.Store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(FlowInstanceStatus.Completed, snapshot.Status);
        Assert.NotNull(completed?.Result);
        Assert.Equal(new TestResult(42), harness.Serializer.Deserialize<TestResult>(completed.Result));
    }

    [Fact]
    public async Task HostedService_PumpsRegisteredFlowsFromDependencyInjection()
    {
        var flowName = new FlowName("hosted-service-flow");
        var store = new InMemorySpindleStore();
        var clock = new FakeSpindleClock(DateTimeOffset.Parse("2026-06-28T12:00:00Z"));
        var services = new ServiceCollection();

        services.AddSingleton<ISpindleStore>(store);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSpindleFlow<HostedServiceFlow, TestRequest, TestResult>(flowName);
        services.AddSpindleWorker(options =>
        {
            options.PollInterval = TimeSpan.FromMilliseconds(10);
            options.MaxConcurrentFlowInstances = 2;
            options.MaxStepsPerFlowPerTick = 1;
            options.WorkerId = "hosted-test-worker";
        });

        await using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<ISpindleRuntime>();
        var hosted = provider.GetServices<IHostedService>().Single();

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(41));

        await hosted.StartAsync(CancellationToken.None);

        try
        {
            await WaitForCompletedAsync(runtime, handle.InstanceId);
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        var instance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var serializer = provider.GetRequiredService<ISpindleSerializer>();

        Assert.NotNull(instance?.Result);
        Assert.Equal(new TestResult(42), serializer.Deserialize<TestResult>(instance.Result));
    }

    private static async Task WaitForCompletedAsync(
        ISpindleRuntime runtime,
        FlowInstanceId instanceId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!cts.IsCancellationRequested)
        {
            var snapshot = await runtime.GetInstanceAsync(instanceId, cts.Token);
            if (snapshot?.Status == FlowInstanceStatus.Completed)
            {
                return;
            }

            if (snapshot?.Status is FlowInstanceStatus.Failed or FlowInstanceStatus.Cancelled or FlowInstanceStatus.TimedOut)
            {
                throw new InvalidOperationException(
                    $"Flow instance '{instanceId}' reached terminal status '{snapshot.Status}'.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cts.Token);
        }

        throw new TimeoutException(
            $"Flow instance '{instanceId}' did not complete.");
    }

    private sealed record TestRequest(int Value);

    private sealed record TestResult(int Value);

    private sealed record HandlerRequest(int Value);

    private sealed class DoubleHandler : IStepHandler<HandlerRequest, int>
    {
        public ValueTask<int> ExecuteAsync(
            HandlerRequest request,
            IStepExecutionContext context)
        {
            return ValueTask.FromResult(request.Value * 2);
        }
    }

    private sealed class GateHandler : IStepHandler<HandlerRequest, int>
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<int> ExecuteAsync(
            HandlerRequest request,
            IStepExecutionContext context)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(context.CancellationToken)
                .ConfigureAwait(false);

            return request.Value;
        }
    }

    private sealed class HostedServiceFlow : ISpindleFlow<TestRequest, TestResult>
    {
        public async ValueTask<TestResult> RunAsync(
            IFlowContext context,
            TestRequest request)
        {
            var value = context.Step<int>(
                "add-one",
                "Add one",
                () => ValueTask.FromResult(request.Value + 1));

            return new TestResult(await value);
        }
    }
}

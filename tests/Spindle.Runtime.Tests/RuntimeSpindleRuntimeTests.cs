using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Nodes;
using Spindle.Persistence.Timers;
using Spindle.Runtime.Tests.Stores;
using Spindle.Testing;
using Xunit;
using InMemorySpindleStore = Spindle.Persistence.InMemory.InMemorySpindleStore;

namespace Spindle.Runtime.Tests;

public sealed class RuntimeSpindleRuntimeTests : TestBase
{
    [Fact]
    public async Task StartAsync_PersistsFlowInstance()
    {
        var (runtime, store, serializer) = CreateRuntime();
        var flowName = new FlowName("start-persists-instance");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            (_, request) => ValueTask.FromResult(new TestResult(request.Value + 1)));

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(41));

        var instance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.NotNull(instance);
        Assert.Equal(flowName, instance.FlowName);
        Assert.Equal(new FlowVersion("1"), instance.FlowVersion);
        Assert.Equal(FlowInstanceStatus.Completed, instance.Status);
        Assert.Equal(new TestRequest(41), serializer.Deserialize<TestRequest>(instance.Input));
    }

    [Fact]
    public async Task StepDeclaration_PersistsReadyStep()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("step-declaration");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            (context, _) =>
            {
                context.Step<int>("a", "A", () => ValueTask.FromResult(42));
                return ValueTask.FromResult(new TestResult(0));
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        var steps = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);
        var step = Assert.Single(steps);

        Assert.Equal(new NodeId("a"), step.NodeId);
        Assert.Equal("A", step.Name);
        Assert.Equal(NodeKind.Step, step.Kind);
        Assert.Equal(NodeStatus.Ready, step.Status);
        Assert.Empty(step.Dependencies);
    }

    [Fact]
    public async Task AwaitingIncompleteStep_SuspendsWithoutLeakingInternalException()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("await-suspends");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var step = context.Step<int>("a", "A", () => ValueTask.FromResult(42));
                var value = await step;
                return new TestResult(value);
            });

        FlowInstanceHandle<TestResult>? handle = null;

        var exception = await Record.ExceptionAsync(async () =>
        {
            handle = await runtime.StartAsync<TestRequest, TestResult>(
                flowName,
                new TestRequest(0));
        });

        Assert.Null(exception);
        Assert.NotNull(handle);

        var instance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var step = Assert.Single(await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.NotNull(instance);
        Assert.Equal(FlowInstanceStatus.Waiting, instance.Status);
        Assert.Equal(NodeStatus.Ready, step.Status);
    }

    [Fact]
    public async Task ReadyLocalStep_ExecutesAndPersistsResult()
    {
        var (runtime, store, serializer) = CreateRuntime();
        var flowName = new FlowName("ready-step-executes");
        var options = new StartFlowOptions { IdempotencyKey = "ready-step-executes" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var step = context.Step<int>("a", "A", () => ValueTask.FromResult(42));
                return new TestResult(await step);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var step = Assert.Single(await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.Equal(new TestResult(42), result);
        Assert.Equal(NodeStatus.Completed, step.Status);
        Assert.NotNull(step.Result);
        Assert.Equal(42, serializer.Deserialize<int>(step.Result));
    }

    [Fact]
    public async Task Replay_ReturnsPersistedStepResultAndCompletesFlow()
    {
        var (runtime, store, serializer) = CreateRuntime();
        var flowName = new FlowName("replay-step-result");
        var options = new StartFlowOptions { IdempotencyKey = "replay-step-result" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, request) =>
            {
                var step = context.Step<int>("a", "A", () => ValueTask.FromResult(request.Value + 1));
                var value = await step;
                return new TestResult(value + 1);
            });

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(40),
            options);

        var instance = await store.FlowInstances.GetByIdempotencyKeyAsync(flowName, options.IdempotencyKey!);

        Assert.NotNull(instance);
        Assert.Equal(new TestResult(42), result);
        Assert.Equal(FlowInstanceStatus.Completed, instance.Status);
        Assert.NotNull(instance.Result);
        Assert.Equal(new TestResult(42), serializer.Deserialize<TestResult>(instance.Result));
    }

    [Fact]
    public async Task IndependentSteps_BothBecomeReadyBeforeAwait()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("independent-steps");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var a = context.Step<int>("a", "A", () => ValueTask.FromResult(1));
                var b = context.Step<int>("b", "B", () => ValueTask.FromResult(2));
                await context.WaitAll("wait-all", "Wait for all", a, b);
                return new TestResult(3);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        var nodes = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.Equal(3, nodes.Count);
        Assert.All(
            nodes.Where(node => node.Kind == NodeKind.Step),
            node => Assert.Equal(NodeStatus.Ready, node.Status));
        Assert.Equal(
            NodeStatus.Waiting,
            nodes.Single(node => node.Kind == NodeKind.WaitAll).Status);
    }

    [Fact]
    public async Task DependentStep_WaitsForParentsBeforeRunning()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("dependency-waits");
        var options = new StartFlowOptions { IdempotencyKey = "dependency-waits" };
        var events = new List<string>();

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var a = context.Step<int>("a", "A", () =>
                {
                    events.Add("a");
                    return ValueTask.FromResult(1);
                });
                var b = context.Step<int>("b", "B", () =>
                {
                    events.Add("b");
                    return ValueTask.FromResult(2);
                });
                var c = context.Step<int, int, int>("c", "C", a, b, (left, right) =>
                {
                    events.Add("c");
                    return ValueTask.FromResult(left + right);
                });

                return new TestResult(await c);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var firstReplaySteps = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);
        Assert.Equal(NodeStatus.Pending, firstReplaySteps.Single(step => step.NodeId == new NodeId("c")).Status);

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        Assert.Equal(new TestResult(3), result);
        Assert.True(events.IndexOf("c") > events.IndexOf("a"));
        Assert.True(events.IndexOf("c") > events.IndexOf("b"));
    }

    [Fact]
    public async Task FailedStep_PersistsFailureAndFailsFlow()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("failed-step");
        var options = new StartFlowOptions { IdempotencyKey = "failed-step" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var step = context.Step<int>(
                    "a",
                    "A",
                    () => ValueTask.FromException<int>(new InvalidOperationException("boom")));

                return new TestResult(await step);
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RunAsync<TestRequest, TestResult>(
                    flowName,
                    new TestRequest(0),
                    options)
                .AsTask());

        var instance = await store.FlowInstances.GetByIdempotencyKeyAsync(flowName, options.IdempotencyKey!);
        Assert.NotNull(instance);

        var step = Assert.Single(await store.Nodes.GetByFlowInstanceAsync(instance.InstanceId));

        Assert.Contains("boom", exception.Message);
        Assert.Equal(NodeStatus.Failed, step.Status);
        Assert.Contains("boom", step.Error);
        Assert.Equal(FlowInstanceStatus.Failed, instance.Status);
    }

    [Fact]
    public async Task RunAsync_CompletesSimpleFlow()
    {
        var (runtime, _, _) = CreateRuntime();
        var flowName = new FlowName("run-simple");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, request) =>
            {
                var step = context.Step<int>(
                    "add-one",
                    "Add one",
                    () => ValueTask.FromResult(request.Value + 1));

                return new TestResult(await step);
            });

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(41));

        Assert.Equal(new TestResult(42), result);
    }

    [Fact]
    public async Task StepDeclaration_UsesBulkSnapshotAndCreateBatch()
    {
        var inner = new InMemorySpindleStore();
        var store = new CountingSpindleStore(inner);
        var runtime = new RuntimeSpindleRuntime(store);
        var flowName = new FlowName("bulk-step-declaration");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var steps = Enumerable
                    .Range(0, 10)
                    .Select(index => context.Step<int>(
                        $"step-{index}",
                        $"Step {index}",
                        () => ValueTask.FromResult(index)))
                    .ToArray();

                await context.WaitAll("wait-all", "Wait for all", steps);

                return new TestResult(steps.Length);
            });

        await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        Assert.Equal(2, store.Nodes.GetByFlowInstanceCalls);
        Assert.Equal(0, store.Nodes.GetAsyncCalls);
        Assert.Equal(0, store.Nodes.CreateCalls);
        Assert.Equal(1, store.Nodes.CreateManyCalls);
        Assert.Equal(11, store.Nodes.CreatedInBatches);
    }

    [Fact]
    public async Task ReplayOfExistingSteps_DoesNotCreateStepBatch()
    {
        var inner = new InMemorySpindleStore();
        var store = new CountingSpindleStore(inner);
        var runtime = new RuntimeSpindleRuntime(store);
        var flowName = new FlowName("bulk-step-replay");
        var options = new StartFlowOptions { IdempotencyKey = "bulk-step-replay" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var step = context.Step<int>(
                    "step",
                    "Step",
                    () => ValueTask.FromResult(42));

                return new TestResult(await step);
            });

        await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        store.Nodes.Reset();

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        Assert.Equal(new TestResult(42), result);
        Assert.Equal(0, store.Nodes.CreateCalls);
        Assert.Equal(0, store.Nodes.CreateManyCalls);
    }



}

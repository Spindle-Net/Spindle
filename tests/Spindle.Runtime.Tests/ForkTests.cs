using Spindle;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Snapshot;
using Spindle.Hosting;
using Spindle.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Spindle.Runtime.Tests;

public class ForkTests : TestBase
{

    [Fact(DisplayName = "Fork - Empty fork is silently skipped in one step")]
    public async Task Fork_EmptyCompletesInstantly()
    {
        var (runtime, store, serializer) = CreateRuntime();
        var flowName = new FlowName("fork-empty");
        var options = new StartFlowOptions { IdempotencyKey = "fork-empty" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.Fork("test", async ctx => { });

                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var nodes = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Completed, waitingInstance.Status);
        Assert.Empty(nodes);

        Assert.NotNull(waitingInstance.Result);
        var result = serializer.Deserialize<TestResult>(waitingInstance.Result);
        Assert.Equal(new TestResult(1), result);
    }

    [Fact(DisplayName = "Fork - Creates a step from a fork descriptor")]
    public async Task Fork_StepsAreDeclared()
    {
        var (runtime, store, serializer) = CreateRuntime();
        var flowName = new FlowName("fork-single-step");
        var options = new StartFlowOptions { IdempotencyKey = "fork-single-step" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.Fork("test", async ctx => 
                {
                    await ctx.Step("test", "Test", () => ValueTask.FromResult(0));    
                });

                return new TestResult(2);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingNodes = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        var node = Assert.Single(waitingNodes);
        Assert.Equal(NodeKind.Step, node.Kind);
        Assert.Equal(NodeStatus.Ready, node.Status);

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(2), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
    }

    [Fact(DisplayName = "Fork - Namespaces steps to avoid id conflicts")]
    public async Task Fork_StepsAreNamespaced()
    {
        var (runtime, store, serializer) = CreateRuntime();
        var flowName = new FlowName("fork-single-step");
        var options = new StartFlowOptions { IdempotencyKey = "fork-single-step" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _req) =>
            {
                _ = context.Step("test", "Test", () => ValueTask.FromResult(1));
                await context.Fork("fork", async ctx =>
                {
                    await ctx.Step("test", "Test", () => ValueTask.FromResult(0));
                });

                return new TestResult(3);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingNodes = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.Equal(2, waitingNodes.Count);
        Assert.All(waitingNodes, node => Assert.Equal(NodeKind.Step, node.Kind));
        Assert.All(waitingNodes, node => Assert.Equal("Test", node.Name));
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "test");
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork/test");

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(3), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
    }

    [Fact(DisplayName = "Fork - Executes steps in parallel")]
    public async Task Fork_MultipleAsynchronousPaths()
    {
        var harness = new SpindleTestHarness(hostOptions: new SpindleHostOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            MaxConcurrentFlowInstances = 1,
            MaxFlowInstancesPerTick = 100,
            MaxStepsPerFlowPerTick = 4,
            WorkerId = "test-worker"
        });
        var flowName = new FlowName("fork-async-steps");
        var options = new StartFlowOptions { IdempotencyKey = "fork-async-steps" };

        harness.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _req) =>
            {
                var fork1 = context.Fork("fork1", async ctx => 
                {
                    await ctx.Step("test1", "Test", () => ValueTask.FromResult(1));
                    await ctx.Step("test2", "Test", () => ValueTask.FromResult(2));
                });
                var fork2 = context.Fork("fork2", async ctx =>
                {
                    await ctx.Step("test1", "Test", () => ValueTask.FromResult(1));
                    await ctx.Step("test2", "Test", () => ValueTask.FromResult(2));
                });
                await fork1;
                await fork2;

                return new TestResult(3);
            });

        var handle = await harness.StartFlowAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await harness.Store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingNodes = await harness.Store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.Equal(2, waitingNodes.Count);
        Assert.All(waitingNodes, node => Assert.Equal(NodeKind.Step, node.Kind));
        Assert.All(waitingNodes, node => Assert.Equal("Test", node.Name));
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork1/test1");
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork2/test1");

        await harness.PumpAndWaitOnceAsync(TimeSpan.FromSeconds(5));

        waitingInstance = await harness.Store.FlowInstances.GetAsync(handle.InstanceId);
        waitingNodes = await harness.Store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.Equal(4, waitingNodes.Count);
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork1/test1");
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork1/test2");
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork2/test1");
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork2/test2");

        var snapshot = await harness.PumpUntilCompletedAsync(handle.InstanceId);
        var instance = await harness.Store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.NotNull(snapshot);
        Assert.Equal(FlowInstanceStatus.Completed, snapshot.Status);
        Assert.NotNull(instance?.Result);
        Assert.Equal(new TestResult(3), harness.Serializer.Deserialize<TestResult>(instance.Result));
    }

    [Fact(DisplayName = "Fork - Returns value returned from function")]
    public async Task Fork_ReturnsResultValue()
    {
        var (runtime, store, serializer) = CreateRuntime();
        var flowName = new FlowName("fork-return");
        var options = new StartFlowOptions { IdempotencyKey = "fork-return" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _req) =>
            {
                await context.Step("test1", "First Step", () =>
                    ValueTask.FromResult(new TestResult(1)));

                return await context.Fork("fork", async ctx =>
                {
                    await ctx.Step("test1", "First Step", () => 
                        ValueTask.FromResult(new TestResult(2)));

                    return await ctx.Step("test2", "Second Step", () => 
                        ValueTask.FromResult(new TestResult(3)));
                });
            });

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        Assert.Equal(new TestResult(3), result);
    }

    [Fact(DisplayName = "Fork - Starts and runs fork in background")]
    public async Task Fork_StartsAndRunsInBackground()
    {
        var harness = new SpindleTestHarness(hostOptions: new SpindleHostOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            MaxConcurrentFlowInstances = 1,
            MaxFlowInstancesPerTick = 100,
            MaxStepsPerFlowPerTick = 4,
            WorkerId = "test-worker"
        });
        var flowName = new FlowName("fork-in-background");
        var options = new StartFlowOptions { IdempotencyKey = "fork-in-background" };

        harness.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var fork1 = context.Fork("fork", async ctx =>
                {
                    await ctx.Step("test1", "Test", () => ValueTask.FromResult(1));
                    return await ctx.Step("test2", "Test", () => ValueTask.FromResult(2));
                });
                await context.Delay("delay", TimeSpan.FromMinutes(30));
                await fork1;

                return new TestResult(3);
            });

        var handle = await harness.StartFlowAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await harness.Store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingNodes = await harness.Store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.Equal(2, waitingNodes.Count);
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork/test1");
        Assert.DoesNotContain(waitingNodes, x => x.NodeId.Value == "fork/test2");

        await harness.PumpUntilIdleAsync();

        waitingInstance = await harness.Store.FlowInstances.GetAsync(handle.InstanceId);
        waitingNodes = await harness.Store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.Equal(3, waitingNodes.Count);
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork/test1" && x.Status == NodeStatus.Completed);
        Assert.Contains(waitingNodes, x => x.NodeId.Value == "fork/test2" && x.Status == NodeStatus.Completed);

        harness.AdvanceTimeBy(TimeSpan.FromMinutes(30));

        var snapshot = await harness.PumpUntilCompletedAsync(handle.InstanceId);
        var instance = await harness.Store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.NotNull(snapshot);
        Assert.Equal(FlowInstanceStatus.Completed, snapshot.Status);
        Assert.NotNull(instance?.Result);
        Assert.Equal(new TestResult(3), harness.Serializer.Deserialize<TestResult>(instance.Result));
    }
}

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Snapshot;
using Xunit;

namespace Spindle.Runtime.Tests;

public sealed class ConditionWaitTests : TestBase
{
    [Fact]
    public async Task Condition_IsCheckedImmediatelyAndCompletesFlow()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("condition-immediate");
        var options = new StartFlowOptions { IdempotencyKey = "condition-immediate" };
        var checks = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.WaitForCondition(
                    "ready",
                    "Ready",
                    TimeSpan.FromMinutes(5),
                    () => ValueTask.FromResult(++checks == 1));
                return new TestResult(42);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);
        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);
        var node = Assert.Single(await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.Equal(new TestResult(42), result);
        Assert.Equal(1, checks);
        Assert.Equal(NodeKind.ConditionWait, node.Kind);
        Assert.Equal(NodeStatus.Completed, node.Status);
    }

    [Fact]
    public async Task FalseCondition_SchedulesNextCheckAndSurvivesReplay()
    {
        var initial = DateTimeOffset.Parse("2026-08-25T10:00:00Z");
        var (runtime, store, serializer, clock) = CreateRuntime(initial);
        var flowName = new FlowName("condition-polls");
        var options = new StartFlowOptions { IdempotencyKey = "condition-polls" };
        var checks = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.WaitForCondition(
                    "ready",
                    TimeSpan.FromMinutes(5),
                    () => ValueTask.FromResult(++checks >= 2));
                return new TestResult(checks);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        await Assert.ThrowsAsync<NotSupportedException>(() => runtime
            .RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), options)
            .AsTask());

        var timer = await store.Timers.GetAsync(handle.InstanceId, new NodeId("ready"));
        var waiting = await store.Nodes.GetAsync(handle.InstanceId, new NodeId("ready"));

        Assert.Equal(1, checks);
        Assert.NotNull(timer);
        Assert.Equal(initial.AddMinutes(5), timer.DueAt);
        Assert.Null(timer.FiredAt);
        Assert.Equal(NodeStatus.Waiting, waiting?.Status);

        clock.SetUtcNow(initial.AddMinutes(5));
        var restartedRuntime = new RuntimeSpindleRuntime(
            store,
            options: new RuntimeSpindleOptions
            {
                TimeProvider = clock,
                Serializer = serializer
            });
        restartedRuntime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.WaitForCondition(
                    "ready",
                    TimeSpan.FromMinutes(5),
                    () => ValueTask.FromResult(++checks >= 2));
                return new TestResult(checks);
            });

        var result = await restartedRuntime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        Assert.Equal(new TestResult(2), result);
        Assert.Equal(2, checks);
    }

    [Fact]
    public async Task ConditionTimeout_TimesOutNodeAndFailsFlow()
    {
        var initial = DateTimeOffset.Parse("2026-08-25T10:00:00Z");
        var (runtime, store, _, clock) = CreateRuntime(initial);
        var flowName = new FlowName("condition-timeout");
        var options = new StartFlowOptions { IdempotencyKey = "condition-timeout" };
        var reachedAfterWait = false;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.WaitForCondition(
                        "ready",
                        TimeSpan.FromMinutes(5),
                        () => ValueTask.FromResult(false))
                    .WithTimeout(TimeSpan.FromMinutes(2));
                reachedAfterWait = true;
                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        await Assert.ThrowsAsync<NotSupportedException>(() => runtime
            .RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), options)
            .AsTask());

        var timer = await store.Timers.GetAsync(handle.InstanceId, new NodeId("ready"));
        Assert.Equal(initial.AddMinutes(2), timer?.DueAt);

        clock.SetUtcNow(initial.AddMinutes(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime
            .RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), options)
            .AsTask());

        var node = await store.Nodes.GetAsync(handle.InstanceId, new NodeId("ready"));
        var instance = await store.FlowInstances.GetAsync(handle.InstanceId);
        Assert.Equal(NodeStatus.TimedOut, node?.Status);
        Assert.Equal(FlowInstanceStatus.Failed, instance?.Status);
        Assert.False(reachedAfterWait);
    }

    [Fact]
    public async Task ConditionCallbackException_FailsNodeWithoutPollingAgain()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("condition-error");
        var options = new StartFlowOptions { IdempotencyKey = "condition-error" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.WaitForCondition(
                    "ready",
                    TimeSpan.FromMinutes(5),
                    () => ValueTask.FromException<bool>(new InvalidOperationException("check failed")));
                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime
            .RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), options)
            .AsTask());

        var node = await store.Nodes.GetAsync(handle.InstanceId, new NodeId("ready"));
        Assert.Equal(NodeStatus.Failed, node?.Status);
        Assert.Null(await store.Timers.GetAsync(handle.InstanceId, new NodeId("ready")));
    }

    [Fact]
    public async Task TypedConditionInputs_AreMaterializedInDeclarationOrder()
    {
        var (runtime, _, _) = CreateRuntime();
        var flowName = new FlowName("condition-inputs");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var first = context.Step(
                    "first",
                    "First",
                    () => ValueTask.FromResult(20));
                var second = context.Step(
                    "second",
                    "Second",
                    () => ValueTask.FromResult(22));
                var condition = context.WaitForCondition(
                    "sum-ready",
                    TimeSpan.FromMinutes(1),
                    first,
                    second,
                    (left, right) => ValueTask.FromResult(left + right == 42));
                var result = context.Step<Unit, TestResult>(
                    "result",
                    "Result",
                    condition,
                    _ => ValueTask.FromResult(new TestResult(42)));
                return await result;
            });

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        Assert.Equal(new TestResult(42), result);
    }

    [Fact]
    public async Task ConditionInsideFork_UsesNamespacedNodeId()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("condition-fork");
        var options = new StartFlowOptions { IdempotencyKey = "condition-fork" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.Fork(
                    "branch",
                    async branch =>
                    {
                        await branch.WaitForCondition(
                            "ready",
                            TimeSpan.FromMinutes(1),
                            () => ValueTask.FromResult(true));
                    });
                return new TestResult(42);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);
        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);
        var node = Assert.Single(await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.Equal(new TestResult(42), result);
        Assert.Equal(new NodeId("branch/ready"), node.NodeId);
        Assert.Equal(NodeKind.ConditionWait, node.Kind);
        Assert.Equal(NodeStatus.Completed, node.Status);
    }
}

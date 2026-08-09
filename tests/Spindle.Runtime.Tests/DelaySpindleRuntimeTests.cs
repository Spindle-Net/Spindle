using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Xunit;

namespace Spindle.Runtime.Tests;

public class DelaySpindleRuntimeTests : TestBase
{

    [Fact]
    public async Task Delay_PersistsTimerAndSuspendsFlow()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T10:00:00Z");
        var (runtime, store, _, _) = CreateRuntime(initial);
        var flowName = new FlowName("delay-persists-timer");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.Delay("wait", "Wait", TimeSpan.FromMinutes(5));
                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        var instance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var timer = await store.Timers.GetAsync(handle.InstanceId, new NodeId("wait"));
        var step = Assert.Single(await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.NotNull(instance);
        Assert.Equal(FlowInstanceStatus.Waiting, instance.Status);
        Assert.NotNull(timer);
        Assert.Equal(initial.AddMinutes(5), timer.DueAt);
        Assert.Null(timer.FiredAt);
        Assert.Equal(new NodeId("wait"), step.NodeId);
        Assert.Equal(NodeKind.Timer, step.Kind);
        Assert.Equal(NodeStatus.Waiting, step.Status);
    }

    [Fact]
    public async Task Delay_DoesNotRecomputeDueAtOnReplayBeforeDue()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T10:00:00Z");
        var replayTime = DateTimeOffset.Parse("2026-06-28T10:02:00Z");
        var (runtime, store, _, clock) = CreateRuntime(initial);
        var flowName = new FlowName("delay-stable-due-at");
        var options = new StartFlowOptions { IdempotencyKey = "delay-stable-due-at" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.Delay("wait", "Wait", TimeSpan.FromMinutes(5));
                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        clock.SetUtcNow(replayTime);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            runtime.RunAsync<TestRequest, TestResult>(
                    flowName,
                    new TestRequest(0),
                    options)
                .AsTask());

        var timer = await store.Timers.GetAsync(handle.InstanceId, new NodeId("wait"));

        Assert.NotNull(timer);
        Assert.Equal(initial.AddMinutes(5), timer.DueAt);
        Assert.Null(timer.FiredAt);
    }

    [Fact]
    public async Task Delay_CompletesAfterTimerIsDue()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T10:00:00Z");
        var due = DateTimeOffset.Parse("2026-06-28T10:05:00Z");
        var (runtime, store, _, clock) = CreateRuntime(initial);
        var flowName = new FlowName("delay-completes-after-due");
        var options = new StartFlowOptions { IdempotencyKey = "delay-completes-after-due" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.Delay("wait", "Wait", TimeSpan.FromMinutes(5));
                return new TestResult(42);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        clock.SetUtcNow(due);

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var instance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var timer = await store.Timers.GetAsync(handle.InstanceId, new NodeId("wait"));
        var step = Assert.Single(await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.Equal(new TestResult(42), result);
        Assert.NotNull(instance);
        Assert.Equal(FlowInstanceStatus.Completed, instance.Status);
        Assert.NotNull(timer);
        Assert.Equal(due, timer.FiredAt);
        Assert.Equal(NodeStatus.Completed, step.Status);
        Assert.Equal(due, step.CompletedAt);
    }

    [Fact]
    public async Task DelayUntil_UsesProvidedDueAt()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T10:00:00Z");
        var due = DateTimeOffset.Parse("2026-06-28T10:30:00Z");
        var (runtime, store, _, _) = CreateRuntime(initial);
        var flowName = new FlowName("delay-until-uses-provided-due-at");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.DelayUntil("wait", "Wait", due);
                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        var timer = await store.Timers.GetAsync(handle.InstanceId, new NodeId("wait"));

        Assert.NotNull(timer);
        Assert.Equal(due, timer.DueAt);
    }

}

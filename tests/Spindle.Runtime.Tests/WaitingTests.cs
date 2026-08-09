using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Snapshot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Spindle.Runtime.Tests;

/// <summary>
/// Tests that test the <see cref="IFlowContext"/> step waiting methods
/// </summary>
public class WaitingTests : TestBase
{

    [Fact]
    public async Task WaitAll_SuspendsUntilAllStepsComplete()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("waitall-suspends");
        var options = new StartFlowOptions { IdempotencyKey = "waitall-suspends" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var a = context.Step<int>("a", "A", () => ValueTask.FromResult(1));
                var b = context.Step<int>("b", "B", () => ValueTask.FromResult(2));

                await context.WaitAll("wait-all", "Wait for all", a, b);

                return new TestResult(await a + await b);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingNodes = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.All(
            waitingNodes.Where(node => node.Kind == NodeKind.Step),
            node => Assert.Equal(NodeStatus.Ready, node.Status));
        Assert.Equal(
            NodeStatus.Waiting,
            waitingNodes.Single(node => node.Kind == NodeKind.WaitAll).Status);

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(3), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
    }

    // TODO: Add throw test

    [Fact]
    public async Task WaitNodes_ExposeSpecializedDeclarationHandles()
    {
        var (runtime, _, _) = CreateRuntime();
        var flowName = new FlowName("specialized-wait-nodes");
        DelayNode? delay = null;
        SignalNode<int>? signal = null;
        WaitAllNode? waitAll = null;
        WaitAnyNode? waitAny = null;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                delay = context.Delay("delay", "Delay", TimeSpan.FromMinutes(5));
                signal = context.WaitForSignal<int>(
                    "signal",
                    "Wait for signal",
                    new SignalName("signal"),
                    new CorrelationKey("key"));
                waitAll = context.WaitAll("all", "Wait for all", delay, signal);
                waitAny = context.WaitAny("any", "Wait for any", delay, signal);

                await waitAny;
                return new TestResult(1);
            });

        await runtime.StartAsync<TestRequest, TestResult>(flowName, new TestRequest(0));

        Assert.NotNull(delay);
        Assert.NotNull(signal);
        Assert.NotNull(waitAll);
        Assert.NotNull(waitAny);
        Assert.Equal(new SignalName("signal"), signal.SignalName);
        Assert.Equal(new CorrelationKey("key"), signal.CorrelationKey);
        Assert.Equal(BarrierCompletionMode.Terminal, waitAny.CompletionMode);
        Assert.Equal([delay.Id, signal.Id], waitAny.Inputs.Select(node => node.Id));
    }

    [Fact]
    public async Task WaitNodes_DefaultNameToTheirExplicitId()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("wait-default-names");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var delay = context.Delay("delay", TimeSpan.FromHours(1));
                var signal = context.WaitForSignal<int>(
                    "signal",
                    new SignalName("signal"),
                    new CorrelationKey("key"));
                var all = context.WaitAll("all", delay, signal);
                var any = context.WaitAny(
                    "any",
                    BarrierCompletionMode.SuccessfulOnly,
                    delay,
                    signal);

                Assert.NotNull(all);
                await any;
                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));
        var nodes = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.Equal("delay", nodes.Single(node => node.NodeId == new NodeId("delay")).Name);
        Assert.Equal("signal", nodes.Single(node => node.NodeId == new NodeId("signal")).Name);
        Assert.Equal("all", nodes.Single(node => node.NodeId == new NodeId("all")).Name);
        Assert.Equal("any", nodes.Single(node => node.NodeId == new NodeId("any")).Name);
    }

    [Fact]
    public async Task WaitAny_CanFeedDownstreamStepAndPersistWinningNode()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("waitany-downstream-dependency");
        var options = new StartFlowOptions { IdempotencyKey = "waitany-downstream-dependency" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var assignA = context.Step<int>("assign-a", "Assign Team A", () => ValueTask.FromResult(1));
                var assignB = context.Step<int>("assign-b", "Assign Team B", () => ValueTask.FromResult(1));
                var ackA = context.WaitForSignal(
                    "ack-a",
                    "Wait for Team A acknowledgement",
                    new SignalName("ack"),
                    new CorrelationKey("team-a"));
                var ackB = context.WaitForSignal(
                    "ack-b",
                    "Wait for Team B acknowledgement",
                    new SignalName("ack"),
                    new CorrelationKey("team-b"));
                var timeout = context.Delay("timeout", "Escalation delay", TimeSpan.FromHours(1));
                var winner = context.WaitAny("first", "First acknowledgement or timeout", ackA, ackB, timeout);
                var decide = context.Step<WaitAnyResult, TestResult>(
                    "decide",
                    "Decide whether to escalate",
                    winner,
                    result => ValueTask.FromResult(
                        new TestResult(result.Winner.NodeId == timeout.Id ? 0 : 1)));

                return await decide;
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        await runtime.SignalAsync(
            handle.InstanceId,
            new SignalName("ack"),
            new CorrelationKey("team-b"));

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);
        var nodes = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.Equal(new TestResult(1), result);
        Assert.Equal(NodeStatus.Completed, nodes.Single(node => node.NodeId == new NodeId("first")).Status);
        Assert.Equal(NodeStatus.Waiting, nodes.Single(node => node.NodeId == new NodeId("ack-a")).Status);
        Assert.Equal(NodeStatus.Waiting, nodes.Single(node => node.NodeId == new NodeId("timeout")).Status);
    }

    [Fact]
    public async Task UnitWaitNodes_CanFeedDownstreamStep()
    {
        var initial = DateTimeOffset.Parse("2026-08-09T12:00:00Z");
        var (runtime, _, _, clock) = CreateRuntime(initial);
        var flowName = new FlowName("unit-wait-node-dependency");
        var options = new StartFlowOptions { IdempotencyKey = "unit-wait-node-dependency" };
        var due = initial.AddMinutes(5);

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var delay = context.Delay("delay", "Delay", TimeSpan.FromMinutes(5));
                var signal = context.WaitForSignal<Unit>(
                    "signal",
                    "Signal",
                    new SignalName("ready"),
                    new CorrelationKey("unit"));
                var downstream = context.Step<Unit, Unit, TestResult>(
                    "downstream",
                    "Downstream",
                    delay,
                    signal,
                    (_, _) => ValueTask.FromResult(new TestResult(42)));

                return await downstream;
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        clock.SetUtcNow(due);
        await runtime.SignalAsync(
            handle.InstanceId,
            new SignalName("ready"),
            new CorrelationKey("unit"));

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        Assert.Equal(new TestResult(42), result);
    }

    [Fact]
    public async Task WaitAny_UsesDeclarationOrderWhenInputsAreAlreadyTerminal()
    {
        var (runtime, _, _) = CreateRuntime();
        var flowName = new FlowName("waitany-declaration-order");
        var options = new StartFlowOptions { IdempotencyKey = "waitany-declaration-order" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var first = context.WaitForSignal(
                    "first-signal",
                    "First signal",
                    new SignalName("ready"),
                    new CorrelationKey("first"));
                var second = context.WaitForSignal(
                    "second-signal",
                    "Second signal",
                    new SignalName("ready"),
                    new CorrelationKey("second"));
                var waitAny = context.WaitAny("any", "Wait for either", first, second);
                var result = await waitAny;

                return new TestResult(result.Winner.NodeId == first.Id ? 1 : 2);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);
        await runtime.SignalAsync(handle.InstanceId, new SignalName("ready"), new CorrelationKey("second"));
        await runtime.SignalAsync(handle.InstanceId, new SignalName("ready"), new CorrelationKey("first"));

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        Assert.Equal(new TestResult(1), result);
    }

    [Fact]
    public async Task WaitAll_TerminalModeReportsMixedOutcomes()
    {
        var (runtime, _, _) = CreateRuntime();
        var flowName = new FlowName("waitall-terminal-outcomes");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var succeeded = context.Step<int>("succeeded", "Succeeded", () => ValueTask.FromResult(1));
                var failed = context.Step<int>(
                    "failed",
                    "Failed",
                    () => ValueTask.FromException<int>(new InvalidOperationException("Expected failure")));
                var waitAll = context.WaitAll("all", "Wait for both outcomes", succeeded, failed);
                var inspect = context.Step<WaitAllResult, TestResult>(
                    "inspect",
                    "Inspect outcomes",
                    waitAll,
                    result => ValueTask.FromResult(
                        new TestResult(result.Outcomes.Count(outcome => outcome.Status == NodeStatus.Completed))));

                return await inspect;
            });

        var result = await runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0));

        Assert.Equal(new TestResult(1), result);
    }

    [Fact]
    public async Task WaitAny_SuccessfulOnlySkipsFailedInputs()
    {
        var (runtime, _, _) = CreateRuntime();
        var flowName = new FlowName("waitany-success-only");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var failed = context.Step<int>(
                    "failed",
                    "Failed",
                    () => ValueTask.FromException<int>(new InvalidOperationException("Expected failure")));
                var succeeded = context.Step<int>("succeeded", "Succeeded", () => ValueTask.FromResult(1));
                var waitAny = context.WaitAny(
                    "any",
                    "Wait for a success",
                    BarrierCompletionMode.SuccessfulOnly,
                    failed,
                    succeeded);
                var winner = await waitAny;

                return new TestResult(winner.Winner.NodeId == succeeded.Id ? 1 : 0);
            });

        var result = await runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0));

        Assert.Equal(new TestResult(1), result);
    }

    [Fact]
    public async Task WaitAll_SuccessfulOnlyFailsWhenAnInputFails()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("waitall-success-only-failure");
        var options = new StartFlowOptions { IdempotencyKey = "waitall-success-only-failure" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var succeeded = context.Step<int>("succeeded", "Succeeded", () => ValueTask.FromResult(1));
                var failed = context.Step<int>(
                    "failed",
                    "Failed",
                    () => ValueTask.FromException<int>(new InvalidOperationException("Expected failure")));
                var waitAll = context.WaitAll(
                    "all",
                    "Require every input to succeed",
                    BarrierCompletionMode.SuccessfulOnly,
                    succeeded,
                    failed);

                await waitAll;
                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), options).AsTask());

        var instance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var nodes = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);
        Assert.NotNull(instance);
        Assert.Equal(FlowInstanceStatus.Failed, instance.Status);
        Assert.Equal(NodeStatus.Failed, nodes.Single(node => node.NodeId == new NodeId("all")).Status);
    }

}

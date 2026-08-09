using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Spindle.Runtime.Tests;

public class SignalTests : TestBase
{
    [Fact]
    public async Task WaitForSignal_SuspendsUntilSignalIsSent()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("waitforsignal-suspends");
        var options = new StartFlowOptions { IdempotencyKey = "waitforsignal-suspends" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.WaitForSignal("something", "Wait for something", new SignalName("something"), new CorrelationKey(""));

                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingSteps = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);
        var signalWaits = await store.Signals.GetOpenWaitsAsync(new SignalName("something"));

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        var signalStep = Assert.Single(waitingSteps);
        Assert.Equal(NodeStatus.Waiting, signalStep.Status);
        Assert.Equal(NodeKind.SignalWait, signalStep.Kind);
        var signalWait = Assert.Single(signalWaits);
        Assert.Equal(signalStep.NodeId, signalWait.NodeId);
        Assert.Equal(signalStep.FlowInstanceId, signalWait.FlowInstanceId);
        Assert.Equal(new SignalName("something"), signalWait.SignalName);


        await runtime.SignalAsync(new SignalName("something"), new CorrelationKey(""));

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(1), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
    }

    [Fact]
    public async Task WaitForSignal_RespectsCorrelationKey()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("waitforsignal-respects-correlationkey");
        var options = new StartFlowOptions { IdempotencyKey = "waitforsignal-respects-correlationkey" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.WaitForSignal("key@something", "Wait for something", new SignalName("something"), new CorrelationKey("key"));

                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingSteps = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);
        var signalWaits = await store.Signals.GetOpenWaitsAsync(new SignalName("something"));

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        var signalStep = Assert.Single(waitingSteps);
        Assert.Equal(NodeStatus.Waiting, signalStep.Status);
        Assert.Equal(NodeKind.SignalWait, signalStep.Kind);
        var signalWait = Assert.Single(signalWaits);
        Assert.Equal(signalStep.NodeId, signalWait.NodeId);
        Assert.Equal(signalStep.FlowInstanceId, signalWait.FlowInstanceId);
        Assert.Equal(new SignalName("something"), signalWait.SignalName);
        Assert.Equal(new CorrelationKey("key"), signalWait.CorrelationKey);

        await runtime.SignalAsync(new SignalName("something"), new CorrelationKey("another-key"));

        // Try stepping, nothing should happen
        handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        waitingSteps = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);
        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        signalStep = Assert.Single(waitingSteps);
        Assert.Equal(NodeStatus.Waiting, signalStep.Status);
        Assert.Equal(NodeKind.SignalWait, signalStep.Kind);

        await runtime.SignalAsync(new SignalName("something"), new CorrelationKey("key"));

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(1), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
    }

    [Fact]
    public async Task WaitForSignal_MultipleSignals()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("waitforsignal-multiple-signals");
        var options = new StartFlowOptions { IdempotencyKey = "waitforsignal-multiple-signals" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var s1 = context.WaitForSignal("key1@something", "Wait for something (key1)", new SignalName("something"), new CorrelationKey("key1"));
                var s2 = context.WaitForSignal("key2@something", "Wait for something (key2)", new SignalName("something"), new CorrelationKey("key2"));
                await s1; // It will wake up and suspend on the next await after this
                await s2;

                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingSteps = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);
        var signalWaits = await store.Signals.GetOpenWaitsAsync(new SignalName("something"));

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.Equal(2, waitingSteps.Count);
        foreach (var signalStep in waitingSteps)
        {
            Assert.Equal(NodeStatus.Waiting, signalStep.Status);
            Assert.Equal(NodeKind.SignalWait, signalStep.Kind);
        }
        Assert.Equal(2, signalWaits.Count);
        var first = signalWaits[0];
        var second = signalWaits[1];
        Assert.Equal(new CorrelationKey("key1"), first.CorrelationKey);
        Assert.Equal(new CorrelationKey("key2"), second.CorrelationKey);

        await runtime.SignalAsync(new SignalName("something"), new CorrelationKey("key2"));

        // Stepping here should clear the second wait, effectively nothing should happen
        handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        waitingSteps = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);
        signalWaits = await store.Signals.GetOpenWaitsAsync(new SignalName("something"));
        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.Equal(2, waitingSteps.Count);
        Assert.Equal(NodeStatus.Waiting, waitingSteps[0].Status);
        Assert.Equal(NodeStatus.Completed, waitingSteps[1].Status); // Here the second one should have completed
        var signalWait = Assert.Single(signalWaits);
        Assert.Equal(new CorrelationKey("key1"), signalWait.CorrelationKey);

        await runtime.SignalAsync(new SignalName("something"), new CorrelationKey("key1"));

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(1), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
    }

    [Fact]
    public async Task WaitForSignal_MultipleSignalsAtOnce()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("waitforsignal-multiple-signals");
        var options = new StartFlowOptions { IdempotencyKey = "waitforsignal-multiple-signals" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var s1 = context.WaitForSignal("key1@something", "Wait for something (key1)", new SignalName("something"), new CorrelationKey("key1"));
                var s2 = context.WaitForSignal("key2@something", "Wait for something (key2)", new SignalName("something"), new CorrelationKey("key2"));
                await s1; // It will wake up and suspend on the next await after this
                await s2;

                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingSteps = await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId);
        var signalWaits = await store.Signals.GetOpenWaitsAsync(new SignalName("something"));

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.Equal(2, waitingSteps.Count);
        foreach (var signalStep in waitingSteps)
        {
            Assert.Equal(NodeStatus.Waiting, signalStep.Status);
            Assert.Equal(NodeKind.SignalWait, signalStep.Kind);
        }
        Assert.Equal(2, signalWaits.Count);
        var first = signalWaits[0];
        var second = signalWaits[1];
        Assert.Equal(new CorrelationKey("key1"), first.CorrelationKey);
        Assert.Equal(new CorrelationKey("key2"), second.CorrelationKey);

        await runtime.SignalAsync(new SignalName("something"), new CorrelationKey("key1"));
        await runtime.SignalAsync(new SignalName("something"), new CorrelationKey("key2"));

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(1), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
    }

    [Fact]
    public async Task WaitForSignal_CollectsPayload()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("waitforsignal-collects-payload");
        var options = new StartFlowOptions { IdempotencyKey = "waitforsignal-collects-payload" };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var data = await context.WaitForSignal<int>("data@something", "Wait for data", new SignalName("something"), new CorrelationKey("data"));

                return new TestResult(data * 2);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        await runtime.SignalAsync<int>(new SignalName("something"), new CorrelationKey("data"), 8);

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(16), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
    }

}

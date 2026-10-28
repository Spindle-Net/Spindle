using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.InMemory;
using Xunit;

namespace Spindle.Runtime.Tests;

public class ManualRetryTests : TestBase
{
    private static readonly NodeId Left = new("left");
    private static readonly NodeId Right = new("right");
    private static readonly NodeId Barrier = new("both");

    private static readonly RetryPolicy TwoImmediateAttempts = new()
    {
        MaxAttempts = 2,
        InitialDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero,
        JitterFactor = 0
    };

    [Fact]
    public async Task RetryAsync_FailedFlow_SchedulesStepWithFreshBudgetAndKeepsHistory()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("manual-retry-fresh-budget");
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context
                .Step<int>("left", "Left", () => ++executions < 4
                    ? throw new InvalidOperationException($"failure {executions}")
                    : ValueTask.FromResult(executions))
                .WithRetry(TwoImmediateAttempts)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(runtime, flowName));
        var instanceId = await GetInstanceIdAsync(store, flowName);

        await runtime.RetryAsync(instanceId, Left);

        // Retrying schedules work; it does not run the step.
        var scheduled = await store.Nodes.GetAsync(instanceId, Left);
        var reopened = await store.FlowInstances.GetAsync(instanceId);
        Assert.Equal(2, executions);
        Assert.Equal(NodeStatus.Waiting, scheduled?.Status);
        Assert.Equal(2, scheduled?.RetryAttemptBaseline);
        Assert.NotNull(scheduled?.RetryAt);
        Assert.Equal(FlowInstanceStatus.Running, reopened?.Status);
        Assert.Null(reopened?.Error);
        Assert.Null(reopened?.CompletedAt);

        // The fresh budget allows two more attempts: one failure, then success.
        Assert.Equal(new TestResult(4), await Run(runtime, flowName));

        var node = await store.Nodes.GetAsync(instanceId, Left);
        var attempts = await store.Nodes.GetAttemptsAsync(instanceId, Left);
        Assert.Equal(4, node?.Attempt);
        Assert.Equal([1, 2, 3, 4], attempts.Select(attempt => attempt.Attempt));
        Assert.Equal(
            [NodeStatus.Failed, NodeStatus.Failed, NodeStatus.Failed, NodeStatus.Completed],
            attempts.Select(attempt => attempt.Status));
    }

    [Fact]
    public async Task RetryAsync_WithNodeId_RetriesOnlyThatStepAndReopensBarrier()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("manual-retry-explicit");
        var healthy = false;
        var executions = new Dictionary<string, int> { ["left"] = 0, ["right"] = 0, ["ok"] = 0 };

        RegisterBranchingFlow(runtime, flowName, executions, () => healthy);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(runtime, flowName));
        var instanceId = await GetInstanceIdAsync(store, flowName);
        Assert.Equal(NodeStatus.Failed, (await store.Nodes.GetAsync(instanceId, Barrier))?.Status);

        await runtime.RetryAsync(instanceId, Left);

        var nodes = await GetNodesAsync(store, instanceId);
        Assert.Equal(NodeStatus.Waiting, nodes["left"].Status);
        Assert.Equal(NodeStatus.Failed, nodes["right"].Status);
        Assert.Equal(NodeStatus.Completed, nodes["ok"].Status);
        Assert.Equal(NodeStatus.Waiting, nodes["both"].Status);
        Assert.Null(nodes["both"].Error);

        // The barrier only accepts successful inputs, so it fails again while the right branch is failed.
        healthy = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(runtime, flowName));
        nodes = await GetNodesAsync(store, instanceId);
        Assert.Equal(NodeStatus.Failed, nodes["right"].Status);
        Assert.Equal(NodeStatus.Failed, nodes["both"].Status);

        await runtime.RetryAsync(instanceId, Right);

        Assert.Equal(new TestResult(3), await Run(runtime, flowName));
        nodes = await GetNodesAsync(store, instanceId);
        Assert.All(nodes.Values, node => Assert.Equal(NodeStatus.Completed, node.Status));
        Assert.Equal(1, executions["ok"]);
    }

    [Fact]
    public async Task RetryAsync_WithoutNodeId_RetriesAllFailedStepsAndRecomputesBarrier()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("manual-retry-all");
        var healthy = false;
        var executions = new Dictionary<string, int> { ["left"] = 0, ["right"] = 0, ["ok"] = 0 };

        RegisterBranchingFlow(runtime, flowName, executions, () => healthy);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(runtime, flowName));
        var instanceId = await GetInstanceIdAsync(store, flowName);

        healthy = true;
        await runtime.RetryAsync(instanceId);

        Assert.Equal(new TestResult(3), await Run(runtime, flowName));

        var nodes = await GetNodesAsync(store, instanceId);
        Assert.All(nodes.Values, node => Assert.Equal(NodeStatus.Completed, node.Status));
        Assert.Equal(1, executions["ok"]);
        Assert.Equal(3, executions["left"]);
        Assert.Equal(3, executions["right"]);
    }

    [Fact]
    public async Task RetryAsync_ActiveFlow_LeavesWaitingSiblingUntouched()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("manual-retry-active-sibling");
        var signalName = new SignalName("go");
        var correlationKey = new CorrelationKey("sibling");
        var healthy = false;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var step = context.Step<int>("left", "Left", () => healthy
                    ? ValueTask.FromResult(1)
                    : throw new InvalidOperationException("not yet"));
                var signal = context.WaitForSignal<int>("signal", signalName, correlationKey);

                await context.WaitAll("both", step, signal);
                return new TestResult(await step + await signal);
            });

        await Assert.ThrowsAsync<NotSupportedException>(() => Run(runtime, flowName));
        var instanceId = await GetInstanceIdAsync(store, flowName);
        Assert.Equal(FlowInstanceStatus.Waiting, (await store.FlowInstances.GetAsync(instanceId))?.Status);

        healthy = true;
        await runtime.RetryAsync(instanceId);

        var nodes = await GetNodesAsync(store, instanceId);
        Assert.Equal(NodeStatus.Waiting, nodes["left"].Status);
        Assert.Equal(NodeStatus.Waiting, nodes["signal"].Status);
        Assert.Null(nodes["signal"].RetryAt);

        await runtime.SignalAsync(instanceId, signalName, correlationKey, 41);
        Assert.Equal(new TestResult(42), await Run(runtime, flowName));
    }

    [Fact]
    public async Task RetryAsync_RepeatedCall_RejectsAlreadyScheduledStep()
    {
        var (runtime, store, instanceId) = await StartFailedFlowAsync("manual-retry-repeated");

        await runtime.RetryAsync(instanceId, Left);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RetryAsync(instanceId, Left).AsTask());
        Assert.Contains("Waiting", exception.Message);
        Assert.Equal(2, (await store.Nodes.GetAsync(instanceId, Left))?.RetryAttemptBaseline);
    }

    [Fact]
    public async Task RetryAsync_ConcurrentCalls_ScheduleTheStepOnce()
    {
        var (runtime, store, instanceId) = await StartFailedFlowAsync("manual-retry-concurrent");

        var calls = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => runtime.RetryAsync(instanceId).AsTask()))
            .ToArray();
        var outcomes = await Task.WhenAll(calls.Select(async call =>
        {
            try
            {
                await call;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }));

        Assert.Equal(1, outcomes.Count(succeeded => succeeded));
        Assert.Equal(NodeStatus.Waiting, (await store.Nodes.GetAsync(instanceId, Left))?.Status);
    }

    [Fact]
    public async Task RetryAsync_RejectsMissingFlowAndMissingOrIneligibleNodes()
    {
        var (runtime, _, instanceId) = await StartFailedFlowAsync("manual-retry-invalid-targets");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RetryAsync(new FlowInstanceId("missing")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RetryAsync(instanceId, new NodeId("missing")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RetryAsync(instanceId, new NodeId("ok")).AsTask());
    }

    [Fact]
    public async Task RetryAsync_RejectsCompletedAndCancelledFlows()
    {
        var (runtime, store, _) = CreateRuntime();
        var completedFlow = new FlowName("manual-retry-completed");
        runtime.RegisterFlow<TestRequest, TestResult>(
            completedFlow,
            async (context, _) => new TestResult(await context.Step<int>("left", "Left", () => ValueTask.FromResult(1))));
        await Run(runtime, completedFlow);
        var completedId = await GetInstanceIdAsync(store, completedFlow);

        var (cancelledRuntime, _, cancelledId) = await StartFailedFlowAsync("manual-retry-cancelled");
        await cancelledRuntime.CancelAsync(cancelledId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RetryAsync(completedId).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cancelledRuntime.RetryAsync(cancelledId).AsTask());
    }

    [Fact]
    public async Task RetryAsync_FlowWithoutFailedSteps_IsRejected()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("manual-retry-nothing-failed");
        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.WaitForSignal<int>("signal", new SignalName("never"), new CorrelationKey("never"));
                return new TestResult(0);
            });

        await Assert.ThrowsAsync<NotSupportedException>(() => Run(runtime, flowName));
        var instanceId = await GetInstanceIdAsync(store, flowName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RetryAsync(instanceId).AsTask());
    }

    /// <summary>
    /// Registers a flow with two flaky branches and one healthy branch joined by a barrier that only accepts
    /// successful inputs. While unhealthy, the flaky branches fail after two attempts each.
    /// </summary>
    private static void RegisterBranchingFlow(
        RuntimeSpindleRuntime runtime,
        FlowName flowName,
        Dictionary<string, int> executions,
        Func<bool> healthy)
    {
        ValueTask<int> Execute(string id)
        {
            executions[id]++;
            return healthy() || id == "ok"
                ? ValueTask.FromResult(1)
                : throw new InvalidOperationException($"{id} failed");
        }

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var left = context.Step<int>("left", "Left", () => Execute("left")).WithRetry(TwoImmediateAttempts);
                var right = context.Step<int>("right", "Right", () => Execute("right")).WithRetry(TwoImmediateAttempts);
                var ok = context.Step<int>("ok", "Ok", () => Execute("ok"));

                await context.WaitAll("both", BarrierCompletionMode.SuccessfulOnly, left, right, ok);
                return new TestResult(await left + await right + await ok);
            });
    }

    private static async Task<(RuntimeSpindleRuntime Runtime, InMemorySpindleStore Store, FlowInstanceId InstanceId)> StartFailedFlowAsync(
        string name)
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName(name);
        RegisterBranchingFlow(
            runtime,
            flowName,
            new Dictionary<string, int> { ["left"] = 0, ["right"] = 0, ["ok"] = 0 },
            () => false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(runtime, flowName));
        return (runtime, store, await GetInstanceIdAsync(store, flowName));
    }

    private static Task<TestResult> Run(RuntimeSpindleRuntime runtime, FlowName flowName)
        => runtime.RunAsync<TestRequest, TestResult>(
                flowName,
                new TestRequest(0),
                new StartFlowOptions { IdempotencyKey = flowName.Value })
            .AsTask();

    private static async Task<FlowInstanceId> GetInstanceIdAsync(InMemorySpindleStore store, FlowName flowName)
    {
        var instance = await store.FlowInstances.GetByIdempotencyKeyAsync(flowName, flowName.Value);
        Assert.NotNull(instance);
        return instance.InstanceId;
    }

    private static async Task<Dictionary<string, Persistence.Nodes.NodeInstanceRecord>> GetNodesAsync(
        InMemorySpindleStore store,
        FlowInstanceId instanceId)
        => (await store.Nodes.GetByFlowInstanceAsync(instanceId)).ToDictionary(node => node.NodeId.Value);
}

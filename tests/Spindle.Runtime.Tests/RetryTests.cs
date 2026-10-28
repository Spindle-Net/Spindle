using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.Nodes;
using Spindle.Testing;
using Xunit;

namespace Spindle.Runtime.Tests;

public sealed class RetryTests : TestBase
{
    private static readonly FlowName Name = new("retry-test");
    private static readonly NodeId StepId = new("work");
    private static RetryPolicy Policy(int attempts = 3) => new()
    {
        MaxAttempts = attempts, InitialDelay = TimeSpan.FromSeconds(1), MaxDelay = TimeSpan.FromSeconds(10), JitterFactor = 0
    };

    private static void Register(RuntimeSpindleRuntime runtime, Func<IStepExecutionContext, ValueTask<int>> callback,
        RetryPolicy? retry = null, TimeoutPolicy? timeout = null, bool fluent = true)
    {
        runtime.RegisterFlow<int, int>(Name, async (ctx, _) =>
        {
            var step = ctx.Step("work", "Work", callback,
                fluent ? null : new StepOptions { Retry = retry, Timeout = timeout });
            if (fluent && retry is not null) step = step.WithRetry(retry);
            if (fluent && timeout is not null) step = step.WithTimeout(timeout);
            return await step;
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetriesSuspendThenSucceedOnThirdAttempt(bool fluent)
    {
        var (runtime, store, _, clock) = CreateRuntime(DateTimeOffset.UtcNow);
        var runs = 0;
        Register(runtime, _ => ++runs < 3 ? throw new NotImplementedException("transient") : ValueTask.FromResult(42), Policy(), fluent: fluent);
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        var waiting = await store.Nodes.GetAsync(handle.InstanceId, StepId);
        Assert.Equal(NodeStatus.Waiting, waiting!.Status);
        Assert.Equal(1, waiting.Attempt);
        Assert.Null(waiting.CompletedAt);
        Assert.Equal(clock.UtcNow.AddSeconds(1), waiting.RetryAt);
        Assert.Equal(FlowInstanceStatus.Waiting, (await runtime.GetInstanceAsync(handle.InstanceId))!.Status);
        Assert.Equal(0, await runtime.FireDueTimersAsync());
        clock.AdvanceBy(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await runtime.FireDueTimersAsync());
        Assert.Equal(0, await runtime.FireDueTimersAsync());
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        Assert.Equal(clock.UtcNow.AddSeconds(2), (await store.Nodes.GetAsync(handle.InstanceId, StepId))!.RetryAt);
        clock.AdvanceBy(TimeSpan.FromSeconds(2));
        await runtime.FireDueTimersAsync();
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        Assert.Equal(3, runs);
        Assert.Equal(FlowInstanceStatus.Completed, (await runtime.GetInstanceAsync(handle.InstanceId))!.Status);
        var completed = await store.Nodes.GetAsync(handle.InstanceId, StepId);
        Assert.Equal(3, completed!.Attempt);
        Assert.Null(completed.RetryAt);
        Assert.Null(completed.Error);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    public async Task ExhaustionIsTerminalAndManualRetryGrantsFreshBudget(int maxAttempts, int expectedRuns)
    {
        var (runtime, store, _) = CreateRuntime();
        var runs = 0;
        Register(runtime, _ => { runs++; throw new NotImplementedException("persistent"); },
            maxAttempts == 0 ? null : Policy(maxAttempts) with { InitialDelay = TimeSpan.Zero });
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        for (var i = 0; i < expectedRuns; i++)
        {
            await runtime.FireDueTimersAsync();
            await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        }
        Assert.Equal(expectedRuns, runs);
        Assert.Equal(FlowInstanceStatus.Failed, (await runtime.GetInstanceAsync(handle.InstanceId))!.Status);
        await runtime.RetryAsync(handle.InstanceId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RetryAsync(handle.InstanceId).AsTask());
        for (var i = 0; i < expectedRuns; i++)
        {
            await runtime.FireDueTimersAsync();
            await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        }
        var node = await store.Nodes.GetAsync(handle.InstanceId, StepId);
        Assert.Equal(expectedRuns * 2, node!.Attempt);
        Assert.Equal(expectedRuns, node.RetryAttemptBaseline);
        Assert.Equal(NodeStatus.Failed, node.Status);
    }

    [Theory]
    [InlineData(RetryBackoffKind.Fixed, 1)]
    [InlineData(RetryBackoffKind.Linear, 3)]
    [InlineData(RetryBackoffKind.Exponential, 4)]
    public async Task BackoffUsesAttemptsWithinBudget(RetryBackoffKind kind, int thirdDelay)
    {
        var (runtime, store, _, clock) = CreateRuntime(DateTimeOffset.UtcNow);
        Register(runtime, _ => throw new Exception("retry"), Policy(5) with { BackoffKind = kind });
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        for (var i = 0; i < 3; i++)
        {
            await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
            var node = await store.Nodes.GetAsync(handle.InstanceId, StepId);
            if (i == 2) Assert.Equal(TimeSpan.FromSeconds(thirdDelay), node!.RetryAt - clock.UtcNow);
            clock.AdvanceTo(node!.RetryAt!.Value);
            await runtime.FireDueTimersAsync();
        }
    }

    [Fact]
    public async Task RunAsyncWaitsForRetryUsingFakeTime()
    {
        var (runtime, _, _, clock) = CreateRuntime(DateTimeOffset.UtcNow);
        var runs = 0;
        Register(runtime, _ => ++runs == 1 ? throw new Exception() : ValueTask.FromResult(42), Policy());
        var result = runtime.RunAsync<int, int>(Name, 0).AsTask();
        await Until(() => clock.GetPendingTimers().Contains(clock.UtcNow.AddSeconds(1)));
        Assert.False(result.IsCompleted);
        clock.AdvanceBy(TimeSpan.FromSeconds(1));
        Assert.Equal(42, await result.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(TimeoutBehavior.Fail, NodeStatus.TimedOut, FlowInstanceStatus.Failed)]
    [InlineData(TimeoutBehavior.Cancel, NodeStatus.TimedOut, FlowInstanceStatus.Cancelled)]
    [InlineData(TimeoutBehavior.Continue, NodeStatus.Completed, FlowInstanceStatus.Completed)]
    public async Task TimeoutCancelsThenAbandonsAfterGraceAndIgnoresLateResult(
        TimeoutBehavior behavior, NodeStatus nodeStatus, FlowInstanceStatus flowStatus)
    {
        var (runtime, store, serializer, clock) = CreateRuntime(DateTimeOffset.UtcNow);
        var callback = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Register(runtime, ctx => { ctx.CancellationToken.Register(() => cancelled.TrySetResult()); return new(callback.Task); },
            timeout: new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(10), Behavior = behavior });
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        var advance = runtime.AdvanceInstanceAsync(handle.InstanceId, 100).AsTask();
        await Until(() => clock.GetPendingTimers().Contains(clock.UtcNow.AddSeconds(10)));
        clock.AdvanceBy(TimeSpan.FromSeconds(10));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Until(() => clock.GetPendingTimers().Contains(clock.UtcNow.AddSeconds(5)));
        Assert.False(advance.IsCompleted);
        clock.AdvanceBy(TimeSpan.FromSeconds(4));
        Assert.False(advance.IsCompleted);
        clock.AdvanceBy(TimeSpan.FromSeconds(1));
        await advance.WaitAsync(TimeSpan.FromSeconds(5));
        var node = await store.Nodes.GetAsync(handle.InstanceId, StepId);
        Assert.Equal(nodeStatus, node!.Status);
        Assert.Equal(flowStatus, (await runtime.GetInstanceAsync(handle.InstanceId))!.Status);
        if (behavior == TimeoutBehavior.Continue) Assert.Equal(0, serializer.Deserialize<int>(node.Result!));
        callback.SetResult(999);
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        var afterLateResult = await store.Nodes.GetAsync(handle.InstanceId, StepId);
        Assert.Equal(node.Status, afterLateResult!.Status);
        Assert.Equal(node.Attempt, afterLateResult.Attempt);
        Assert.Equal(node.Result?.Data, afterLateResult.Result?.Data);
    }

    [Fact]
    public async Task CooperativeTimeoutRetriesWithoutWaitingForGrace()
    {
        var (runtime, _, _, clock) = CreateRuntime(DateTimeOffset.UtcNow);
        var calls = 0;
        Register(runtime, async ctx =>
        {
            if (++calls == 1) await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
            return 42;
        }, Policy() with { InitialDelay = TimeSpan.Zero }, new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(10) });
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        var advance = runtime.AdvanceInstanceAsync(handle.InstanceId, 100).AsTask();
        await Until(() => clock.GetPendingTimers().Contains(clock.UtcNow.AddSeconds(10)));
        clock.AdvanceBy(TimeSpan.FromSeconds(10));
        await advance.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.FireDueTimersAsync();
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        Assert.Equal(2, calls);
        Assert.Equal(FlowInstanceStatus.Completed, (await runtime.GetInstanceAsync(handle.InstanceId))!.Status);
    }

    [Fact]
    public async Task CallerCancellationLeavesInterruptedWorkResumable()
    {
        var (runtime, store, _) = CreateRuntime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Register(runtime, async ctx => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ctx.CancellationToken); return 1; }, Policy());
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        using var cancellation = new CancellationTokenSource();
        var advance = runtime.AdvanceInstanceAsync(handle.InstanceId, 100, cancellationToken: cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => advance);
        var node = await store.Nodes.GetAsync(handle.InstanceId, StepId);
        Assert.Equal(NodeStatus.Ready, node!.Status);
        Assert.Equal(1, node.RetryAttemptBaseline);
        Assert.Null(node.RetryAt);
    }

    [Fact]
    public async Task ManualRecoveryReopensFailedBarrierAndPreservesSuccessfulWork()
    {
        var (runtime, store, _) = CreateRuntime();
        var goodRuns = 0;
        var failing = true;
        runtime.RegisterFlow<int, int>(Name, async (ctx, _) =>
        {
            var good = ctx.Step("good", "Good", () => ValueTask.FromResult(++goodRuns));
            var bad = ctx.Step<int>("work", "Work", () => failing ? throw new Exception("bad") : ValueTask.FromResult(42));
            await ctx.WaitAll("all", BarrierCompletionMode.SuccessfulOnly, good, bad);
            return await bad;
        });
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        Assert.Equal(NodeStatus.Failed, (await store.Nodes.GetAsync(handle.InstanceId, new NodeId("all")))!.Status);
        failing = false;
        await runtime.RetryAsync(handle.InstanceId, StepId);
        await runtime.FireDueTimersAsync();
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        Assert.Equal(1, goodRuns);
        Assert.Equal(FlowInstanceStatus.Completed, (await runtime.GetInstanceAsync(handle.InstanceId))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RetryAsync(handle.InstanceId).AsTask());
    }

    [Fact]
    public async Task InvalidPoliciesFailBeforeAnyStepExecutes()
    {
        var invalid = new[] { Policy(0), Policy() with { InitialDelay = TimeSpan.FromSeconds(-1) },
            Policy() with { JitterFactor = double.NaN }, Policy() with { BackoffKind = (RetryBackoffKind)99 },
            Policy() with { MaxDelay = TimeSpan.Zero } };
        foreach (var policy in invalid)
        {
            var (runtime, store, _) = CreateRuntime();
            Register(runtime, _ => throw new Xunit.Sdk.XunitException("Must not run"), policy);
            var handle = await runtime.StartAsync<int, int>(Name, 0);
            Assert.Equal(FlowInstanceStatus.Failed, (await runtime.GetInstanceAsync(handle.InstanceId))!.Status);
            Assert.All(await store.Nodes.GetByFlowInstanceAsync(handle.InstanceId), n => Assert.Equal(0, n.Attempt));
        }
    }

    [Fact]
    public async Task ManualRecoveryWhileFlowWaitsPreservesCompletedBarrier()
    {
        var (runtime, store, _) = CreateRuntime();
        var failing = true;
        runtime.RegisterFlow<int, int>(Name, async (ctx, _) =>
        {
            var work = ctx.Step<int>("work", "Work", () => failing ? throw new Exception("bad") : ValueTask.FromResult(42));
            await ctx.WaitAll("terminal", work);
            await ctx.Delay("later", TimeSpan.FromHours(1));
            return await work;
        });
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        var barrier = await store.Nodes.GetAsync(handle.InstanceId, new NodeId("terminal"));
        Assert.Equal(NodeStatus.Completed, barrier!.Status);
        Assert.Equal(FlowInstanceStatus.Waiting, (await runtime.GetInstanceAsync(handle.InstanceId))!.Status);
        failing = false;
        await runtime.RetryAsync(handle.InstanceId, StepId);
        await runtime.FireDueTimersAsync();
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        Assert.Equal(NodeStatus.Completed, (await store.Nodes.GetAsync(handle.InstanceId, StepId))!.Status);
        Assert.Equal(barrier.CompletedAt, (await store.Nodes.GetAsync(handle.InstanceId, barrier.NodeId))!.CompletedAt);
    }

    [Fact]
    public async Task ConcurrentRecoverySchedulesOnlyOneNewBudget()
    {
        var (runtime, store, _) = CreateRuntime();
        Register(runtime, _ => throw new Exception("failed"));
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            await Record.ExceptionAsync(() => runtime.RetryAsync(handle.InstanceId, StepId).AsTask())));
        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result is InvalidOperationException);
        Assert.Equal(1, (await store.Nodes.GetAsync(handle.InstanceId, StepId))!.RetryAttemptBaseline);
        Assert.Equal(1, await runtime.FireDueTimersAsync());
    }

    [Fact]
    public async Task CancelledFlowDiscardsPendingRetryTimerAndRejectsManualRecovery()
    {
        var (runtime, store, _, clock) = CreateRuntime(DateTimeOffset.UtcNow);
        Register(runtime, _ => throw new Exception("failed"), Policy());
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        await runtime.CancelAsync(handle.InstanceId);
        clock.AdvanceBy(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await runtime.FireDueTimersAsync());
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        Assert.Equal(1, (await store.Nodes.GetAsync(handle.InstanceId, StepId))!.Attempt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RetryAsync(handle.InstanceId).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RetryAsync(new FlowInstanceId("missing")).AsTask());
    }

    [Theory]
    [InlineData(StepDispatchMode.Immediate)]
    [InlineData(StepDispatchMode.LocalWorker)]
    public async Task ZeroDelayPolicyWorksInBothLocalDispatchModes(StepDispatchMode mode)
    {
        var (runtime, _, _) = CreateRuntime();
        var calls = 0;
        runtime.RegisterFlow<int, int>(Name, async (ctx, _) => await ctx.Step<int>("work", "Work", () =>
                ++calls < 3 ? throw new Exception() : ValueTask.FromResult(42))
            .WithOptions(options => options with { DispatchMode = mode })
            .WithRetry(Policy() with { InitialDelay = TimeSpan.Zero }));
        Assert.Equal(42, await runtime.RunAsync<int, int>(Name, 0));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ManualRecoveryDoesNotWaitForRunningSiblingCallback()
    {
        var (runtime, store, _) = CreateRuntime();
        var sibling = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        runtime.RegisterFlow<int, int>(Name, async (ctx, _) =>
        {
            var slow = ctx.Step("slow", "Slow", () => new ValueTask<int>(sibling.Task));
            var work = ctx.Step<int>("work", "Work", () => ++attempts == 1
                ? throw new Exception("failed") : ValueTask.FromResult(42));
            await ctx.WaitAll("all", BarrierCompletionMode.SuccessfulOnly, slow, work);
            return await work;
        });
        var handle = await runtime.StartAsync<int, int>(Name, 0);
        var advance = runtime.AdvanceInstanceAsync(handle.InstanceId, 100).AsTask();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((await store.Nodes.GetAsync(handle.InstanceId, StepId))!.Status != NodeStatus.Failed)
            await Task.Delay(1, deadline.Token);
        await runtime.RetryAsync(handle.InstanceId, StepId).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(sibling.Task.IsCompleted);
        Assert.False(advance.IsCompleted);
        sibling.SetResult(1);
        await advance.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.FireDueTimersAsync();
        await runtime.AdvanceInstanceAsync(handle.InstanceId, 100);
        Assert.Equal(2, attempts);
        Assert.Equal(FlowInstanceStatus.Completed, (await runtime.GetInstanceAsync(handle.InstanceId))!.Status);
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(1, deadline.Token);
    }
}

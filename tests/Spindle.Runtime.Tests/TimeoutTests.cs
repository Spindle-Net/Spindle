using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.InMemory;
using Spindle.Testing;
using Xunit;

namespace Spindle.Runtime.Tests;

public class TimeoutTests : TestBase
{
    private static readonly NodeId StepId = new("slow");
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    private static readonly RetryPolicy ImmediateRetry = new()
    {
        MaxAttempts = 2,
        InitialDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero,
        JitterFactor = 0
    };

    [Fact]
    public async Task Timeout_Fail_CooperativeCallback_EndsStepAsTimedOut()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-fail-cooperative");
        var cancelled = false;

        Register(runtime, flowName, TimeoutBehavior.Fail, retry: null, async context =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }

            return 1;
        });

        var run = Run(runtime, flowName);
        await WaitForTimersAsync(clock);
        clock.Advance(StepTimeout);

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));

        var node = await GetStepAsync(store, flowName);
        var attempt = Assert.Single(await store.Nodes.GetAttemptsAsync(node.FlowInstanceId, StepId));
        Assert.True(cancelled);
        Assert.Equal(NodeStatus.TimedOut, node.Status);
        Assert.Equal(NodeStatus.TimedOut, attempt.Status);
        Assert.Contains("timed out", node.Error);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task Timeout_Fail_WithRetryBudget_RetriesAndSucceeds()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-fail-retry-success");
        var executions = 0;

        Register(runtime, flowName, TimeoutBehavior.Fail, ImmediateRetry, async context =>
        {
            if (++executions == 1)
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }

            return executions;
        });

        var run = Run(runtime, flowName);
        await WaitForTimersAsync(clock);
        clock.Advance(StepTimeout);

        Assert.Equal(new TestResult(2), await run.WaitAsync(TimeSpan.FromSeconds(10)));

        var node = await GetStepAsync(store, flowName);
        var attempts = await store.Nodes.GetAttemptsAsync(node.FlowInstanceId, StepId);
        Assert.Equal(NodeStatus.Completed, node.Status);
        Assert.Equal([NodeStatus.TimedOut, NodeStatus.Completed], attempts.Select(attempt => attempt.Status));
    }

    [Fact]
    public async Task Timeout_Fail_ExhaustsRetryBudget()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-fail-exhausted");
        var executions = 0;

        Register(runtime, flowName, TimeoutBehavior.Fail, ImmediateRetry, async context =>
        {
            executions++;
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
            return 1;
        });

        var run = Run(runtime, flowName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DriveAsync(run, clock, StepTimeout));

        var node = await GetStepAsync(store, flowName);
        Assert.Equal(2, executions);
        Assert.Equal(NodeStatus.TimedOut, node.Status);
        Assert.Equal(2, node.Attempt);
    }

    [Fact]
    public async Task Timeout_UncooperativeCallback_IsAbandonedAfterGracePeriod()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-grace-expiry");
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        Register(runtime, flowName, TimeoutBehavior.Fail, retry: null, async _ => await release.Task);

        var run = Run(runtime, flowName);
        await WaitForTimersAsync(clock);
        clock.Advance(StepTimeout);

        // The callback ignored cancellation, so the runtime now waits for the grace period.
        await WaitForTimersAsync(clock);
        Assert.False(run.IsCompleted);
        clock.Advance(GracePeriod);

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));

        // A result produced after abandonment is discarded.
        release.SetResult(42);
        var node = await GetStepAsync(store, flowName);
        Assert.Equal(NodeStatus.TimedOut, node.Status);
        Assert.Null(node.Result);
    }

    [Fact]
    public async Task Timeout_LateFailureAfterAbandonment_DoesNotChangeOutcome()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-late-failure");
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        Register(runtime, flowName, TimeoutBehavior.Fail, retry: null, async _ => await release.Task);

        var run = Run(runtime, flowName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DriveAsync(run, clock, StepTimeout));

        release.SetException(new InvalidOperationException("late failure"));
        var node = await GetStepAsync(store, flowName);
        Assert.Equal(NodeStatus.TimedOut, node.Status);
        Assert.DoesNotContain("late failure", node.Error);
    }

    [Fact]
    public async Task Timeout_ResultReturnedDuringCleanup_IsTreatedAsTimeout()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-late-success");

        Register(runtime, flowName, TimeoutBehavior.Fail, retry: null, async context =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
            }

            return 42;
        });

        var run = Run(runtime, flowName);
        await WaitForTimersAsync(clock);
        clock.Advance(StepTimeout);

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(NodeStatus.TimedOut, (await GetStepAsync(store, flowName)).Status);
    }

    [Fact]
    public async Task Timeout_Cancel_CancelsFlowWithoutRetrying()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-cancel");
        var executions = 0;

        Register(runtime, flowName, TimeoutBehavior.Cancel, ImmediateRetry, async context =>
        {
            executions++;
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
            return 1;
        });

        var run = Run(runtime, flowName);
        await WaitForTimersAsync(clock);
        clock.Advance(StepTimeout);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));

        var node = await GetStepAsync(store, flowName);
        var instance = await store.FlowInstances.GetAsync(node.FlowInstanceId);
        Assert.Equal(1, executions);
        Assert.Equal(NodeStatus.TimedOut, node.Status);
        Assert.Equal(FlowInstanceStatus.Cancelled, instance?.Status);
    }

    [Fact]
    public async Task Timeout_Continue_CompletesWithDefaultAndReleasesDependents()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-continue");
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var slow = context
                    .Step<int>("slow", "Slow", async stepContext =>
                    {
                        executions++;
                        await Task.Delay(Timeout.Infinite, stepContext.CancellationToken);
                        return 7;
                    })
                    .WithTimeout(Policy(TimeoutBehavior.Continue))
                    .WithRetry(ImmediateRetry);
                var next = context.Step<int, int>("next", "Next", slow, value => ValueTask.FromResult(value + 100));

                return new TestResult(await next);
            });

        var run = Run(runtime, flowName);
        await WaitForTimersAsync(clock);
        clock.Advance(StepTimeout);

        Assert.Equal(new TestResult(100), await run.WaitAsync(TimeSpan.FromSeconds(10)));

        var node = await GetStepAsync(store, flowName);
        var attempt = Assert.Single(await store.Nodes.GetAttemptsAsync(node.FlowInstanceId, StepId));
        Assert.Equal(1, executions);
        Assert.Equal(NodeStatus.Completed, node.Status);
        Assert.Equal(NodeStatus.TimedOut, attempt.Status);
    }

    [Fact]
    public async Task Timeout_CallbackFinishingBeforeDeadline_CompletesAndDisposesTimer()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-not-reached");

        Register(runtime, flowName, TimeoutBehavior.Fail, retry: null, _ => ValueTask.FromResult(5));

        Assert.Equal(new TestResult(5), await Run(runtime, flowName));
        Assert.Equal(NodeStatus.Completed, (await GetStepAsync(store, flowName)).Status);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task CallerCancellation_InterruptsAttemptAndLeavesStepResumable()
    {
        var (runtime, store, clock) = Create();
        var flowName = new FlowName("timeout-caller-cancellation");
        var executions = 0;
        using var cancellation = new CancellationTokenSource();

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context
                .Step<int>("slow", "Slow", async stepContext =>
                {
                    if (++executions == 1)
                    {
                        cancellation.Cancel();
                        await Task.Delay(Timeout.Infinite, stepContext.CancellationToken);
                    }

                    return executions;
                })
                .WithRetry(RetryPolicy.None)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName), cancellation.Token).AsTask());

        var interrupted = await GetStepAsync(store, flowName);
        var instance = await store.FlowInstances.GetAsync(interrupted.FlowInstanceId);
        Assert.Equal(NodeStatus.Ready, interrupted.Status);
        Assert.Equal(NodeStatus.Cancelled, Assert.Single(await store.Nodes.GetAttemptsAsync(interrupted.FlowInstanceId, StepId)).Status);
        Assert.NotEqual(FlowInstanceStatus.Failed, instance?.Status);

        // The interrupted attempt does not use up the single attempt that RetryPolicy.None allows.
        Assert.Equal(new TestResult(2), await Run(runtime, flowName));
        Assert.Equal(2, (await GetStepAsync(store, flowName)).Attempt);
    }

    private static (RuntimeSpindleRuntime Runtime, InMemorySpindleStore Store, FakeSpindleClock Clock) Create()
    {
        var (runtime, store, _, clock) = CreateRuntime(DateTimeOffset.Parse("2026-06-28T12:00:00Z"));
        return (runtime, store, clock);
    }

    private static TimeoutPolicy Policy(TimeoutBehavior behavior) => new()
    {
        Timeout = StepTimeout,
        CancellationGracePeriod = GracePeriod,
        Behavior = behavior
    };

    private static void Register(
        RuntimeSpindleRuntime runtime,
        FlowName flowName,
        TimeoutBehavior behavior,
        RetryPolicy? retry,
        Func<IStepExecutionContext, ValueTask<int>> execute)
    {
        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context.Step(
                "slow",
                "Slow",
                execute,
                new StepOptions { Timeout = Policy(behavior), Retry = retry })));
    }

    private static Task<TestResult> Run(RuntimeSpindleRuntime runtime, FlowName flowName)
        => runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask();

    private static StartFlowOptions KeyFor(FlowName flowName) => new() { IdempotencyKey = flowName.Value };

    private static async Task<Persistence.Nodes.NodeInstanceRecord> GetStepAsync(InMemorySpindleStore store, FlowName flowName)
    {
        var instance = await store.FlowInstances.GetByIdempotencyKeyAsync(flowName, flowName.Value);
        Assert.NotNull(instance);
        var node = await store.Nodes.GetAsync(instance.InstanceId, StepId);
        Assert.NotNull(node);
        return node;
    }
}

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.InMemory;
using Spindle.Persistence.Timers;
using Spindle.Testing;
using Xunit;

namespace Spindle.Runtime.Tests;

public class RetryTests : TestBase
{
    private static readonly NodeId StepId = new("something");

    private static readonly RetryPolicy ImmediateRetry = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero,
        JitterFactor = 0
    };

    [Fact]
    public async Task Retry_PersistentFailure_ExhaustsExactlyThreeAttempts()
    {
        var (runtime, store, _, clock) = CreateRuntime(DateTimeOffset.Parse("2026-06-28T12:00:00Z"));
        var flowName = new FlowName("retry-persistent-failure");
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.Step<int>("something", "test", () =>
                    {
                        executions++;
                        throw new NotImplementedException();
                    })
                    .WithRetry(RetryPolicy.Exponential(3));

                return new TestResult(1);
            });

        var run = runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DriveAsync(run, clock, TimeSpan.FromSeconds(10)));

        var instance = await GetInstanceIdAsync(store, flowName);
        var node = await store.Nodes.GetAsync(instance, StepId);
        var attempts = await store.Nodes.GetAttemptsAsync(instance, StepId);

        Assert.Contains("failed", exception.Message);
        Assert.Equal(3, executions);
        Assert.NotNull(node);
        Assert.Equal(NodeStatus.Failed, node.Status);
        Assert.Equal(3, node.Attempt);
        Assert.Null(node.RetryAt);
        Assert.Equal([1, 2, 3], attempts.Select(attempt => attempt.Attempt));
        Assert.All(attempts, attempt =>
        {
            Assert.Equal(NodeStatus.Failed, attempt.Status);
            Assert.NotNull(attempt.CompletedAt);
        });
    }

    [Fact]
    public async Task Retry_TwoFailuresThenSuccess_CompletesOnThirdAttempt()
    {
        var (runtime, store, _, clock) = CreateRuntime(DateTimeOffset.Parse("2026-06-28T12:00:00Z"));
        var flowName = new FlowName("retry-eventual-success");
        var run = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var value = await context.Step<int>("something", "test", () =>
                    {
                        if (++run < 3)
                        {
                            throw new NotImplementedException();
                        }

                        return ValueTask.FromResult(run);
                    })
                    .WithRetry(RetryPolicy.Exponential(3));

                return new TestResult(value);
            });

        var result = await DriveAsync(
            runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask(),
            clock,
            TimeSpan.FromSeconds(10));

        var instance = await GetInstanceIdAsync(store, flowName);
        var node = await store.Nodes.GetAsync(instance, StepId);
        var attempts = await store.Nodes.GetAttemptsAsync(instance, StepId);

        Assert.Equal(new TestResult(3), result);
        Assert.NotNull(node);
        Assert.Equal(NodeStatus.Completed, node.Status);
        Assert.Equal(3, node.Attempt);
        Assert.Null(node.Error);
        Assert.Null(node.RetryAt);
        Assert.Equal(
            [NodeStatus.Failed, NodeStatus.Failed, NodeStatus.Completed],
            attempts.Select(attempt => attempt.Status));
    }

    [Fact]
    public async Task Retry_FailedAttempt_WaitsDurablyUntilBackoffElapses()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T12:00:00Z");
        var (runtime, store, _, clock) = CreateRuntime(initial);
        var flowName = new FlowName("retry-waits-durably");
        var policy = ImmediateRetry with
        {
            BackoffKind = RetryBackoffKind.Fixed,
            InitialDelay = TimeSpan.FromMinutes(1),
            MaxDelay = TimeSpan.FromMinutes(1)
        };
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context
                .Step<int>("something", "test", () => ++executions == 1
                    ? throw new InvalidOperationException("first attempt fails")
                    : ValueTask.FromResult(executions))
                .WithRetry(policy)));

        var run = runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask();
        await WaitForTimersAsync(clock);

        var instanceId = await GetInstanceIdAsync(store, flowName);
        var waiting = await store.Nodes.GetAsync(instanceId, StepId);
        var timer = await store.Timers.GetAsync(instanceId, StepId);
        var snapshot = await runtime.GetInstanceAsync(instanceId);

        Assert.False(run.IsCompleted);
        Assert.NotNull(waiting);
        Assert.Equal(NodeStatus.Waiting, waiting.Status);
        Assert.Equal(initial.AddMinutes(1), waiting.RetryAt);
        Assert.Equal("first attempt fails", waiting.Error);
        Assert.Equal(initial.AddMinutes(1), timer?.DueAt);
        Assert.Equal(initial.AddMinutes(1), Assert.Single(snapshot!.Nodes).RetryAt);
        Assert.Equal(FlowInstanceStatus.Waiting, snapshot.Status);

        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(new TestResult(2), await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task Retry_PolicyFromInitialStepOptions_IsPersisted()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("retry-initial-options");
        var timeout = new TimeoutPolicy { Timeout = TimeSpan.FromMinutes(1) };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context.Step<int>(
                "something",
                "test",
                () => ValueTask.FromResult(1),
                new StepOptions { Retry = ImmediateRetry, Timeout = timeout })));

        await runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName));

        var node = await store.Nodes.GetAsync(await GetInstanceIdAsync(store, flowName), StepId);
        Assert.Equal(ImmediateRetry, node?.RetryPolicy);
        Assert.Equal(timeout, node?.TimeoutPolicy);
    }

    [Fact]
    public async Task Retry_PolicyFromFluentOptions_IsPersisted()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("retry-fluent-options");
        var timeout = new TimeoutPolicy { Timeout = TimeSpan.FromMinutes(1), Behavior = TimeoutBehavior.Continue };

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context
                .Step<int>("something", "test", () => ValueTask.FromResult(1))
                .WithRetry(ImmediateRetry)
                .WithTimeout(timeout)));

        await runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName));

        var node = await store.Nodes.GetAsync(await GetInstanceIdAsync(store, flowName), StepId);
        Assert.Equal(ImmediateRetry, node?.RetryPolicy);
        Assert.Equal(timeout, node?.TimeoutPolicy);
    }

    [Fact]
    public async Task Retry_ReplayKeepsPolicyPersistedAtFirstDeclaration()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("retry-persisted-policy-wins");
        var replays = 0;
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                // Later replays ask for more attempts; the first declaration's policy must still apply.
                var policy = ImmediateRetry with { MaxAttempts = ++replays == 1 ? 2 : 10 };
                await context.Step<int>("something", "test", () =>
                    {
                        executions++;
                        throw new InvalidOperationException("always fails");
                    })
                    .WithRetry(policy);

                return new TestResult(0);
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask());

        var node = await store.Nodes.GetAsync(await GetInstanceIdAsync(store, flowName), StepId);
        Assert.True(replays > 1);
        Assert.Equal(2, executions);
        Assert.Equal(2, node?.RetryPolicy?.MaxAttempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_MissingOrNonePolicy_RunsOnce(bool useNonePolicy)
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName($"retry-single-attempt-{useNonePolicy}");
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var step = context.Step<int>("something", "test", () =>
                {
                    executions++;
                    throw new InvalidOperationException("fails");
                });

                return new TestResult(await (useNonePolicy ? step.WithRetry(RetryPolicy.None) : step));
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask());

        var node = await store.Nodes.GetAsync(await GetInstanceIdAsync(store, flowName), StepId);
        Assert.Equal(1, executions);
        Assert.Equal(NodeStatus.Failed, node?.Status);
    }

    [Fact]
    public async Task Retry_InvalidPolicy_FailsFlowWithoutRunningStep()
    {
        var (runtime, _, _) = CreateRuntime();
        var flowName = new FlowName("retry-invalid-policy");
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context
                .Step<int>("something", "test", () => ValueTask.FromResult(++executions))
                .WithRetry(new RetryPolicy { MaxAttempts = 0 })));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask());

        Assert.Contains(nameof(RetryPolicy.MaxAttempts), exception.Message);
        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task Retry_CallbackTimeoutException_IsRetried()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("retry-callback-timeout-exception");
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context
                .Step<int>("something", "test", () => ++executions == 1
                    ? throw new TimeoutException("downstream timed out")
                    : ValueTask.FromResult(executions))
                .WithRetry(ImmediateRetry)));

        var result = await runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName));

        var attempts = await store.Nodes.GetAttemptsAsync(await GetInstanceIdAsync(store, flowName), StepId);
        Assert.Equal(new TestResult(2), result);
        Assert.Equal([NodeStatus.Failed, NodeStatus.Completed], attempts.Select(attempt => attempt.Status));
    }

    [Fact]
    public async Task Retry_ResultSerializationFailure_IsNotRetried()
    {
        var (runtime, store, _) = CreateRuntime();
        var flowName = new FlowName("retry-serialization-failure");
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context
                    .Step<Unserializable>("something", "test", () =>
                    {
                        executions++;
                        return ValueTask.FromResult(new Unserializable());
                    })
                    .WithRetry(ImmediateRetry);

                return new TestResult(0);
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask());

        var node = await store.Nodes.GetAsync(await GetInstanceIdAsync(store, flowName), StepId);
        Assert.Equal(1, executions);
        Assert.Equal(NodeStatus.Failed, node?.Status);
        Assert.Null(node?.RetryAt);
    }

    [Fact]
    public async Task Retry_PendingRetry_SuspendsBarrierAndKeepsCompletedSibling()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T12:00:00Z");
        var (runtime, store, _, clock) = CreateRuntime(initial);
        var flowName = new FlowName("retry-barrier-suspends");
        var policy = ImmediateRetry with
        {
            BackoffKind = RetryBackoffKind.Fixed,
            InitialDelay = TimeSpan.FromMinutes(1),
            MaxDelay = TimeSpan.FromMinutes(1)
        };
        var siblingExecutions = 0;
        var flakyExecutions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                var sibling = context.Step<int>("sibling", "Sibling", () => ValueTask.FromResult(++siblingExecutions));
                var flaky = context
                    .Step<int>("flaky", "Flaky", () => ++flakyExecutions == 1
                        ? throw new InvalidOperationException("flaky")
                        : ValueTask.FromResult(10))
                    .WithRetry(policy);
                var dependent = context.Step<int, int>("dependent", "Dependent", flaky, value => ValueTask.FromResult(value + 1));

                await context.WaitAll("both", BarrierCompletionMode.SuccessfulOnly, sibling, flaky);
                return new TestResult(await sibling + await dependent);
            });

        var run = runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask();
        await WaitForTimersAsync(clock);

        var instanceId = await GetInstanceIdAsync(store, flowName);
        var nodes = (await store.Nodes.GetByFlowInstanceAsync(instanceId)).ToDictionary(node => node.NodeId.Value);

        Assert.Equal(NodeStatus.Completed, nodes["sibling"].Status);
        Assert.Equal(NodeStatus.Waiting, nodes["flaky"].Status);
        Assert.Equal(NodeStatus.Pending, nodes["dependent"].Status);
        Assert.Equal(NodeStatus.Waiting, nodes["both"].Status);

        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(new TestResult(12), await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, siblingExecutions);
        Assert.Equal(2, flakyExecutions);
    }

    [Fact]
    public async Task Retry_TimerFiresOnceAndIgnoresStaleTimers()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T12:00:00Z");
        var (runtime, store, _, clock) = CreateRuntime(initial);
        var flowName = new FlowName("retry-timer-fires-once");
        var instanceId = await StartWithPendingRetryAsync(runtime, clock, flowName, TimeSpan.FromMinutes(1));

        // A timer whose due time does not match the pending retry must not wake the step.
        await store.Timers.CreateAsync(new TimerRecord
        {
            FlowInstanceId = instanceId,
            NodeId = StepId,
            DueAt = initial.AddSeconds(30),
            CreatedAt = initial
        });
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, await runtime.FireDueTimersAsync(instanceId));
        Assert.Equal(NodeStatus.Waiting, (await store.Nodes.GetAsync(instanceId, StepId))?.Status);

        await store.Timers.CreateAsync(new TimerRecord
        {
            FlowInstanceId = instanceId,
            NodeId = StepId,
            DueAt = initial.AddMinutes(1),
            CreatedAt = initial
        });
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(1, await runtime.FireDueTimersAsync(instanceId));
        Assert.Equal(0, await runtime.FireDueTimersAsync(instanceId));

        var ready = await store.Nodes.GetAsync(instanceId, StepId);
        Assert.Equal(NodeStatus.Ready, ready?.Status);
        Assert.Null(ready?.RetryAt);
    }

    [Fact]
    public async Task Retry_TimerDoesNotReopenCancelledFlow()
    {
        var (runtime, store, _, clock) = CreateRuntime(DateTimeOffset.Parse("2026-06-28T12:00:00Z"));
        var flowName = new FlowName("retry-timer-cancelled-flow");
        var instanceId = await StartWithPendingRetryAsync(runtime, clock, flowName, TimeSpan.FromMinutes(1));

        await runtime.CancelAsync(instanceId);
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(0, await runtime.FireDueTimersAsync(instanceId));
        Assert.Equal(NodeStatus.Waiting, (await store.Nodes.GetAsync(instanceId, StepId))?.Status);
        Assert.NotNull((await store.Timers.GetAsync(instanceId, StepId))?.FiredAt);
    }

    [Fact]
    public async Task RunAsync_WaitingForRetry_DoesNotConsumeIterations()
    {
        var store = new InMemorySpindleStore();
        var clock = new FakeSpindleClock(DateTimeOffset.Parse("2026-06-28T12:00:00Z"));
        var runtime = new RuntimeSpindleRuntime(
            store,
            options: new RuntimeSpindleOptions { TimeProvider = clock, MaxRunIterations = 4 });
        var flowName = new FlowName("retry-wait-iterations");
        var executions = 0;

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context
                .Step<int>("something", "test", () => ++executions < 3
                    ? throw new InvalidOperationException("not yet")
                    : ValueTask.FromResult(executions))
                .WithRetry(new RetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromMinutes(1), JitterFactor = 0 })));

        // Three executing iterations and the replay that completes the flow fit in four iterations.
        // The two waits for retries in between would exceed the limit if they were counted.
        var result = await DriveAsync(
            runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), KeyFor(flowName)).AsTask(),
            clock,
            TimeSpan.FromMinutes(1));

        Assert.Equal(new TestResult(3), result);
    }

    [Fact]
    public async Task RunAsync_CancelledWhileWaitingForRetry_LeavesRetryPending()
    {
        var (runtime, store, _, clock) = CreateRuntime(DateTimeOffset.Parse("2026-06-28T12:00:00Z"));
        var flowName = new FlowName("retry-wait-cancelled");
        using var cancellation = new CancellationTokenSource();

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context
                .Step<int>("something", "test", () => throw new InvalidOperationException("fails"))
                .WithRetry(new RetryPolicy { InitialDelay = TimeSpan.FromMinutes(1) })));

        var run = runtime.RunAsync<TestRequest, TestResult>(
                flowName,
                new TestRequest(0),
                KeyFor(flowName),
                cancellation.Token)
            .AsTask();
        await WaitForTimersAsync(clock);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        var instanceId = await GetInstanceIdAsync(store, flowName);
        var node = await store.Nodes.GetAsync(instanceId, StepId);
        Assert.Equal(NodeStatus.Waiting, node?.Status);
        Assert.NotNull(node?.RetryAt);
        Assert.Equal(FlowInstanceStatus.Waiting, (await store.FlowInstances.GetAsync(instanceId))?.Status);
    }

    /// <summary>
    /// Starts a flow whose step fails once and waits for a retry after <paramref name="delay"/>.
    /// </summary>
    private static async Task<FlowInstanceId> StartWithPendingRetryAsync(
        RuntimeSpindleRuntime runtime,
        FakeSpindleClock clock,
        FlowName flowName,
        TimeSpan delay)
    {
        var options = KeyFor(flowName);
        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) => new TestResult(await context
                .Step<int>("something", "test", () => throw new InvalidOperationException("fails"))
                .WithRetry(ImmediateRetry with { BackoffKind = RetryBackoffKind.Fixed, InitialDelay = delay, MaxDelay = delay })));

        using var cancellation = new CancellationTokenSource();
        var run = runtime.RunAsync<TestRequest, TestResult>(flowName, new TestRequest(0), options, cancellation.Token).AsTask();
        await WaitForTimersAsync(clock);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        var handle = await runtime.EnqueueAsync<TestRequest, TestResult>(flowName, new TestRequest(0), options);
        return handle.InstanceId;
    }

    private static StartFlowOptions KeyFor(FlowName flowName) => new() { IdempotencyKey = flowName.Value };

    private static async Task<FlowInstanceId> GetInstanceIdAsync(InMemorySpindleStore store, FlowName flowName)
    {
        var instance = await store.FlowInstances.GetByIdempotencyKeyAsync(flowName, flowName.Value);
        Assert.NotNull(instance);
        return instance.InstanceId;
    }

    private sealed record Unserializable
    {
        public int Value => throw new NotSupportedException("This value cannot be serialized.");
    }
}

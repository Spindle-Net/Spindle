using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Steps;
using Spindle.Persistence.Timers;
using Spindle.Testing;
using Xunit;
using InMemorySpindleStore = Spindle.Persistence.InMemory.InMemorySpindleStore;

namespace Spindle.Runtime.Tests;

public sealed class RuntimeSpindleRuntimeTests
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

        var steps = await store.Steps.GetByFlowInstanceAsync(handle.InstanceId);
        var step = Assert.Single(steps);

        Assert.Equal(new StepId("a"), step.StepId);
        Assert.Equal("A", step.Name);
        Assert.Equal(StepKind.Step, step.Kind);
        Assert.Equal(StepStatus.Ready, step.Status);
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
        var step = Assert.Single(await store.Steps.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.NotNull(instance);
        Assert.Equal(FlowInstanceStatus.Waiting, instance.Status);
        Assert.Equal(StepStatus.Ready, step.Status);
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

        var step = Assert.Single(await store.Steps.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.Equal(new TestResult(42), result);
        Assert.Equal(StepStatus.Completed, step.Status);
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
                await context.WaitAll(a, b);
                return new TestResult(3);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        var steps = await store.Steps.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.Equal(2, steps.Count);
        Assert.All(steps, step => Assert.Equal(StepStatus.Ready, step.Status));
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

        var firstReplaySteps = await store.Steps.GetByFlowInstanceAsync(handle.InstanceId);
        Assert.Equal(StepStatus.Pending, firstReplaySteps.Single(step => step.StepId == new StepId("c")).Status);

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        Assert.Equal(new TestResult(3), result);
        Assert.True(events.IndexOf("c") > events.IndexOf("a"));
        Assert.True(events.IndexOf("c") > events.IndexOf("b"));
    }

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

                await context.WaitAll(a, b);

                return new TestResult(await a + await b);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var waitingInstance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var waitingSteps = await store.Steps.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.NotNull(waitingInstance);
        Assert.Equal(FlowInstanceStatus.Waiting, waitingInstance.Status);
        Assert.All(waitingSteps, step => Assert.Equal(StepStatus.Ready, step.Status));

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        var completedInstance = await store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(new TestResult(3), result);
        Assert.NotNull(completedInstance);
        Assert.Equal(FlowInstanceStatus.Completed, completedInstance.Status);
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

        var step = Assert.Single(await store.Steps.GetByFlowInstanceAsync(instance.InstanceId));

        Assert.Contains("boom", exception.Message);
        Assert.Equal(StepStatus.Failed, step.Status);
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
    public async Task Delay_PersistsTimerAndSuspendsFlow()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T10:00:00Z");
        var (runtime, store, _, _) = CreateRuntime(initial);
        var flowName = new FlowName("delay-persists-timer");

        runtime.RegisterFlow<TestRequest, TestResult>(
            flowName,
            async (context, _) =>
            {
                await context.Delay("wait", TimeSpan.FromMinutes(5));
                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        var instance = await store.FlowInstances.GetAsync(handle.InstanceId);
        var timer = await store.Timers.GetAsync(handle.InstanceId, new StepId("wait"));
        var step = Assert.Single(await store.Steps.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.NotNull(instance);
        Assert.Equal(FlowInstanceStatus.Waiting, instance.Status);
        Assert.NotNull(timer);
        Assert.Equal(initial.AddMinutes(5), timer.DueAt);
        Assert.Null(timer.FiredAt);
        Assert.Equal(new StepId("wait"), step.StepId);
        Assert.Equal(StepKind.Timer, step.Kind);
        Assert.Equal(StepStatus.Waiting, step.Status);
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
                await context.Delay("wait", TimeSpan.FromMinutes(5));
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

        var timer = await store.Timers.GetAsync(handle.InstanceId, new StepId("wait"));

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
                await context.Delay("wait", TimeSpan.FromMinutes(5));
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
        var timer = await store.Timers.GetAsync(handle.InstanceId, new StepId("wait"));
        var step = Assert.Single(await store.Steps.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.Equal(new TestResult(42), result);
        Assert.NotNull(instance);
        Assert.Equal(FlowInstanceStatus.Completed, instance.Status);
        Assert.NotNull(timer);
        Assert.Equal(due, timer.FiredAt);
        Assert.Equal(StepStatus.Completed, step.Status);
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
                await context.DelayUntil("wait", due);
                return new TestResult(1);
            });

        var handle = await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        var timer = await store.Timers.GetAsync(handle.InstanceId, new StepId("wait"));

        Assert.NotNull(timer);
        Assert.Equal(due, timer.DueAt);
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

                await context.WaitAll(steps);

                return new TestResult(steps.Length);
            });

        await runtime.StartAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0));

        Assert.Equal(1, store.Steps.GetByFlowInstanceCalls);
        Assert.Equal(0, store.Steps.GetAsyncCalls);
        Assert.Equal(0, store.Steps.CreateCalls);
        Assert.Equal(1, store.Steps.CreateManyCalls);
        Assert.Equal(10, store.Steps.CreatedInBatches);
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

        store.Steps.Reset();

        var result = await runtime.RunAsync<TestRequest, TestResult>(
            flowName,
            new TestRequest(0),
            options);

        Assert.Equal(new TestResult(42), result);
        Assert.Equal(0, store.Steps.CreateCalls);
        Assert.Equal(0, store.Steps.CreateManyCalls);
    }

    private static (RuntimeSpindleRuntime Runtime, InMemorySpindleStore Store, JsonSpindleSerializer Serializer) CreateRuntime()
    {
        var defaultNow = DateTimeOffset.Parse("2026-06-28T12:00:00Z");
        var (runtime, store, serializer, _) = CreateRuntime(defaultNow);
        return (runtime, store, serializer);
    }

    private static (RuntimeSpindleRuntime Runtime, InMemorySpindleStore Store, JsonSpindleSerializer Serializer, FakeSpindleClock Clock) CreateRuntime(
        DateTimeOffset initialUtcNow)
    {
        var store = new InMemorySpindleStore();
        var serializer = new JsonSpindleSerializer();
        var clock = new FakeSpindleClock(initialUtcNow);
        var runtime = new RuntimeSpindleRuntime(
            store,
            options: new RuntimeSpindleOptions
            {
                TimeProvider = clock,
                Serializer = serializer
            });

        return (runtime, store, serializer, clock);
    }

    private sealed record TestRequest(int Value);

    private sealed record TestResult(int Value);

    private sealed class CountingSpindleStore : ISpindleStore
    {
        private readonly ISpindleStore _inner;

        public CountingSpindleStore(
            ISpindleStore inner)
        {
            _inner = inner;
            Steps = new CountingStepStore(inner.Steps);
        }

        public IFlowDefinitionStore FlowDefinitions => _inner.FlowDefinitions;

        public IFlowInstanceStore FlowInstances => _inner.FlowInstances;

        public CountingStepStore Steps { get; }

        IStepStore ISpindleStore.Steps => Steps;

        public ITimerStore Timers => _inner.Timers;

        public ISignalStore Signals => _inner.Signals;

        public IOutboxStore Outbox => _inner.Outbox;

        public IInboxStore Inbox => _inner.Inbox;

        public ILeaseStore Leases => _inner.Leases;

        public IExecutionHistoryStore History => _inner.History;

        public ValueTask<TResult> ExecuteAsync<TResult>(
            Func<ISpindleStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            return _inner.ExecuteAsync(
                (session, storeCancellationToken) =>
                    operation(new CountingStoreSession(session, Steps), storeCancellationToken),
                cancellationToken);
        }
    }

    private sealed class CountingStoreSession(
        ISpindleStoreSession inner,
        CountingStepStore steps)
        : ISpindleStoreSession
    {
        public IFlowDefinitionStore FlowDefinitions => inner.FlowDefinitions;

        public IFlowInstanceStore FlowInstances => inner.FlowInstances;

        public IStepStore Steps => steps;

        public ITimerStore Timers => inner.Timers;

        public ISignalStore Signals => inner.Signals;

        public IOutboxStore Outbox => inner.Outbox;

        public IInboxStore Inbox => inner.Inbox;

        public ILeaseStore Leases => inner.Leases;

        public IExecutionHistoryStore History => inner.History;
    }

    private sealed class CountingStepStore(
        IStepStore inner)
        : IStepStore
    {
        public int CreateCalls { get; private set; }

        public int CreateManyCalls { get; private set; }

        public int CreatedInBatches { get; private set; }

        public int GetAsyncCalls { get; private set; }

        public int GetManyCalls { get; private set; }

        public int GetByFlowInstanceCalls { get; private set; }

        public int GetReadyStepsCalls { get; private set; }

        public int MarkReadyCalls { get; private set; }

        public int MarkRunningCalls { get; private set; }

        public int MarkWaitingCalls { get; private set; }

        public int MarkCompletedCalls { get; private set; }

        public int MarkFailedCalls { get; private set; }

        public void Reset()
        {
            CreateCalls = 0;
            CreateManyCalls = 0;
            CreatedInBatches = 0;
            GetAsyncCalls = 0;
            GetManyCalls = 0;
            GetByFlowInstanceCalls = 0;
            GetReadyStepsCalls = 0;
            MarkReadyCalls = 0;
            MarkRunningCalls = 0;
            MarkWaitingCalls = 0;
            MarkCompletedCalls = 0;
            MarkFailedCalls = 0;
        }

        public ValueTask CreateAsync(
            StepInstanceRecord step,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return inner.CreateAsync(step, cancellationToken);
        }

        public ValueTask CreateManyAsync(
            IReadOnlyList<StepInstanceRecord> steps,
            CancellationToken cancellationToken = default)
        {
            CreateManyCalls++;
            CreatedInBatches += steps.Count;
            return inner.CreateManyAsync(steps, cancellationToken);
        }

        public ValueTask<StepInstanceRecord?> GetAsync(
            FlowInstanceId flowInstanceId,
            StepId stepId,
            CancellationToken cancellationToken = default)
        {
            GetAsyncCalls++;
            return inner.GetAsync(flowInstanceId, stepId, cancellationToken);
        }

        public ValueTask<IReadOnlyList<StepInstanceRecord>> GetManyAsync(
            FlowInstanceId flowInstanceId,
            IReadOnlyList<StepId> stepIds,
            CancellationToken cancellationToken = default)
        {
            GetManyCalls++;
            return inner.GetManyAsync(flowInstanceId, stepIds, cancellationToken);
        }

        public ValueTask<IReadOnlyList<StepInstanceRecord>> GetByFlowInstanceAsync(
            FlowInstanceId flowInstanceId,
            CancellationToken cancellationToken = default)
        {
            GetByFlowInstanceCalls++;
            return inner.GetByFlowInstanceAsync(flowInstanceId, cancellationToken);
        }

        public ValueTask<IReadOnlyList<StepInstanceRecord>> GetReadyStepsAsync(
            int maxCount,
            CancellationToken cancellationToken = default)
        {
            GetReadyStepsCalls++;
            return inner.GetReadyStepsAsync(maxCount, cancellationToken);
        }

        public ValueTask MarkReadyAsync(
            FlowInstanceId flowInstanceId,
            StepId stepId,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            MarkReadyCalls++;
            return inner.MarkReadyAsync(flowInstanceId, stepId, updatedAt, cancellationToken);
        }

        public ValueTask MarkRunningAsync(
            FlowInstanceId flowInstanceId,
            StepId stepId,
            StepAttemptId attemptId,
            string workerId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken = default)
        {
            MarkRunningCalls++;
            return inner.MarkRunningAsync(flowInstanceId, stepId, attemptId, workerId, startedAt, cancellationToken);
        }

        public ValueTask MarkWaitingAsync(
            FlowInstanceId flowInstanceId,
            StepId stepId,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            MarkWaitingCalls++;
            return inner.MarkWaitingAsync(flowInstanceId, stepId, updatedAt, cancellationToken);
        }

        public ValueTask MarkCompletedAsync(
            FlowInstanceId flowInstanceId,
            StepId stepId,
            SerializedPayload? result,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            MarkCompletedCalls++;
            return inner.MarkCompletedAsync(flowInstanceId, stepId, result, completedAt, cancellationToken);
        }

        public ValueTask MarkFailedAsync(
            FlowInstanceId flowInstanceId,
            StepId stepId,
            string error,
            DateTimeOffset failedAt,
            DateTimeOffset? retryAt,
            CancellationToken cancellationToken = default)
        {
            MarkFailedCalls++;
            return inner.MarkFailedAsync(flowInstanceId, stepId, error, failedAt, retryAt, cancellationToken);
        }
    }

}

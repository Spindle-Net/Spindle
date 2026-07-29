using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Hosting;
using Spindle.Persistence;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Testing;
using Xunit;

namespace Spindle.Hosting.Tests;

public sealed class SqliteHostedWorkflowTests
{
    [Fact]
    public async Task HostedWorker_CompletesDependencyGraphAndPersistsResults()
    {
        await using var application = await SqliteWorkflowApplication.StartAsync(
            services => services.AddSpindleFlow<CalculationFlow, CalculationRequest, CalculationResult>(
                CalculationFlow.Name));

        var handle = await application.Runtime.StartAsync<CalculationRequest, CalculationResult>(
            CalculationFlow.Name,
            new CalculationRequest(20));

        var snapshot = await application.WaitForTerminalStatusAsync(handle.InstanceId);
        var instance = await application.Store.FlowInstances.GetAsync(handle.InstanceId);
        var steps = await application.Store.Steps.GetByFlowInstanceAsync(handle.InstanceId);

        Assert.Equal(FlowInstanceStatus.Completed, snapshot.Status);
        Assert.All(snapshot.Steps, step => Assert.Equal(StepStatus.Completed, step.Status));
        Assert.Equal(3, snapshot.Steps.Count);
        Assert.Equal(3, steps.Count);
        Assert.NotNull(instance?.Result);
        Assert.Equal(
            new CalculationResult(Doubled: 40, Incremented: 21, Total: 61),
            application.Serializer.Deserialize<CalculationResult>(instance.Result));
    }

    [Fact]
    public async Task HostedWorker_ResumesDelayedWorkflowAfterClockAdvances()
    {
        var initial = DateTimeOffset.Parse("2026-07-29T10:00:00Z");
        var clock = new FakeSpindleClock(initial);
        await using var application = await SqliteWorkflowApplication.StartAsync(
            services => services.AddSpindleFlow<DelayedFlow, DelayedRequest, DelayedResult>(
                DelayedFlow.Name),
            clock);

        Assert.Same(clock, application.TimeProvider);

        var handle = await application.Runtime.StartAsync<DelayedRequest, DelayedResult>(
            DelayedFlow.Name,
            new DelayedRequest("ready"));

        var waiting = await application.WaitForStatusAsync(
            handle.InstanceId,
            FlowInstanceStatus.Waiting);
        var pendingTimer = await application.Store.Timers.GetAsync(
            handle.InstanceId,
            new StepId("delay"));

        Assert.Equal(FlowInstanceStatus.Waiting, waiting.Status);
        Assert.NotNull(pendingTimer);
        Assert.Equal(initial.AddMinutes(5), pendingTimer.DueAt);
        Assert.Null(pendingTimer.FiredAt);

        clock.AdvanceBy(TimeSpan.FromMinutes(5));

        Assert.Single(await application.Store.Timers.GetDueAsync(clock.GetUtcNow(), maxCount: 10));
        await application.Pump.RunOnceAsync();

        var completed = await application.WaitForTerminalStatusAsync(handle.InstanceId);
        var firedTimer = await application.Store.Timers.GetAsync(
            handle.InstanceId,
            new StepId("delay"));
        var instance = await application.Store.FlowInstances.GetAsync(handle.InstanceId);

        Assert.Equal(FlowInstanceStatus.Completed, completed.Status);
        Assert.Equal(clock.GetUtcNow(), firedTimer?.FiredAt);
        Assert.NotNull(instance?.Result);
        Assert.Equal(
            new DelayedResult("ready-after-delay"),
            application.Serializer.Deserialize<DelayedResult>(instance.Result));
    }

    [Fact]
    public async Task HostedWorker_MarksWorkflowFailedWhenStepThrows()
    {
        await using var application = await SqliteWorkflowApplication.StartAsync(
            services => services.AddSpindleFlow<FailingFlow, FailingRequest, FailingResult>(
                FailingFlow.Name));

        var handle = await application.Runtime.StartAsync<FailingRequest, FailingResult>(
            FailingFlow.Name,
            new FailingRequest("planned failure"));

        var snapshot = await application.WaitForTerminalStatusAsync(handle.InstanceId);
        var instance = await application.Store.FlowInstances.GetAsync(handle.InstanceId);
        var step = Assert.Single(
            await application.Store.Steps.GetByFlowInstanceAsync(handle.InstanceId));

        Assert.Equal(FlowInstanceStatus.Failed, snapshot.Status);
        Assert.Equal(StepStatus.Failed, step.Status);
        Assert.Contains("planned failure", step.Error);
        Assert.Contains("planned failure", instance?.Error);
        Assert.Null(instance?.Result);
    }

    private sealed record CalculationRequest(int Value);

    private sealed record CalculationResult(
        int Doubled,
        int Incremented,
        int Total);

    private sealed class CalculationFlow
        : ISpindleFlow<CalculationRequest, CalculationResult>
    {
        public static FlowName Name { get; } = new("sqlite-calculation");

        public async ValueTask<CalculationResult> RunAsync(
            IFlowContext context,
            CalculationRequest request)
        {
            var doubled = context.Step<int>(
                "double",
                "Double value",
                () => ValueTask.FromResult(request.Value * 2));
            var incremented = context.Step<int>(
                "increment",
                "Increment value",
                () => ValueTask.FromResult(request.Value + 1));
            var total = context.Step<int, int, int>(
                "total",
                "Add results",
                doubled,
                incremented,
                (left, right) => ValueTask.FromResult(left + right));

            return new CalculationResult(
                await doubled,
                await incremented,
                await total);
        }
    }

    private sealed record DelayedRequest(string Value);

    private sealed record DelayedResult(string Value);

    private sealed class DelayedFlow
        : ISpindleFlow<DelayedRequest, DelayedResult>
    {
        public static FlowName Name { get; } = new("sqlite-delay");

        public async ValueTask<DelayedResult> RunAsync(
            IFlowContext context,
            DelayedRequest request)
        {
            await context.Delay("delay", TimeSpan.FromMinutes(5));

            return new DelayedResult($"{request.Value}-after-delay");
        }
    }

    private sealed record FailingRequest(string Message);

    private sealed record FailingResult(string Value);

    private sealed class FailingFlow
        : ISpindleFlow<FailingRequest, FailingResult>
    {
        public static FlowName Name { get; } = new("sqlite-failure");

        public async ValueTask<FailingResult> RunAsync(
            IFlowContext context,
            FailingRequest request)
        {
            var failed = context.Step<int>(
                "fail",
                "Fail deliberately",
                () => ValueTask.FromException<int>(
                    new InvalidOperationException(request.Message)));

            return new FailingResult((await failed).ToString());
        }
    }

    private sealed class SqliteWorkflowApplication : IAsyncDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly SqliteConnection _databaseAnchor;
        private readonly IHost _host;

        private SqliteWorkflowApplication(
            SqliteConnection databaseAnchor,
            IHost host)
        {
            _databaseAnchor = databaseAnchor;
            _host = host;
            Runtime = host.Services.GetRequiredService<ISpindleRuntime>();
            Store = host.Services.GetRequiredService<ISpindleStore>();
            Serializer = host.Services.GetRequiredService<ISpindleSerializer>();
            Pump = host.Services.GetRequiredService<ISpindleRuntimePump>();
            TimeProvider = host.Services.GetRequiredService<TimeProvider>();
        }

        public ISpindleRuntime Runtime { get; }

        public ISpindleStore Store { get; }

        public ISpindleSerializer Serializer { get; }

        public ISpindleRuntimePump Pump { get; }

        public TimeProvider TimeProvider { get; }

        public static async Task<SqliteWorkflowApplication> StartAsync(
            Action<IServiceCollection> configureWorkflows,
            TimeProvider? timeProvider = null)
        {
            var connectionString =
                $"Data Source=SpindleHostedTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var databaseAnchor = new SqliteConnection(connectionString);
            await databaseAnchor.OpenAsync();

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSpindleSqlite(connectionString);

            if (timeProvider != null)
            {
                builder.Services.AddSingleton<TimeProvider>(timeProvider);
            }

            configureWorkflows(builder.Services);
            builder.Services.AddSpindleWorker(options =>
            {
                options.PollInterval = TimeSpan.FromMilliseconds(10);
                options.MaxConcurrentFlowInstances = 4;
                options.MaxFlowInstancesPerTick = 20;
                options.MaxStepsPerFlowPerTick = 10;
                options.WorkerId = "sqlite-hosted-test-worker";
            });

            var host = builder.Build();

            try
            {
                var contextFactory = host.Services
                    .GetRequiredService<IDbContextFactory<SpindleDbContext>>();
                await using var database = await contextFactory.CreateDbContextAsync();
                await database.Database.MigrateAsync();
                await host.StartAsync();

                return new SqliteWorkflowApplication(databaseAnchor, host);
            }
            catch
            {
                host.Dispose();
                await databaseAnchor.DisposeAsync();
                throw;
            }
        }

        public Task<FlowInstanceSnapshot> WaitForTerminalStatusAsync(
            FlowInstanceId instanceId)
        {
            return WaitForSnapshotAsync(
                instanceId,
                snapshot => snapshot.Status is
                    FlowInstanceStatus.Completed or
                    FlowInstanceStatus.Failed or
                    FlowInstanceStatus.Cancelled or
                    FlowInstanceStatus.TimedOut);
        }

        public Task<FlowInstanceSnapshot> WaitForStatusAsync(
            FlowInstanceId instanceId,
            FlowInstanceStatus status)
        {
            return WaitForSnapshotAsync(
                instanceId,
                snapshot => snapshot.Status == status);
        }

        public async ValueTask DisposeAsync()
        {
            using var cts = new CancellationTokenSource(Timeout);
            await _host.StopAsync(cts.Token);
            _host.Dispose();
            await _databaseAnchor.DisposeAsync();
        }

        private async Task<FlowInstanceSnapshot> WaitForSnapshotAsync(
            FlowInstanceId instanceId,
            Func<FlowInstanceSnapshot, bool> predicate)
        {
            using var cts = new CancellationTokenSource(Timeout);

            try
            {
                while (true)
                {
                    var snapshot = await Runtime.GetInstanceAsync(instanceId, cts.Token);
                    if (snapshot != null && predicate(snapshot))
                    {
                        return snapshot;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(10), cts.Token);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Flow instance '{instanceId}' did not reach the expected status within {Timeout}.");
            }
        }
    }
}

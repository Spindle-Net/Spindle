using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Testing;
using Xunit;

namespace Spindle.Hosting.Tests;

public sealed class SqliteRetryTests : IAsyncLifetime
{
    private static readonly FlowName FlowName = new("sqlite-retry");
    private static readonly StartFlowOptions Options = new() { IdempotencyKey = "sqlite-retry" };
    private static readonly NodeId StepId = new("flaky");
    private static readonly RetryPolicy OneMinuteRetry = new()
    {
        MaxAttempts = 2,
        BackoffKind = RetryBackoffKind.Fixed,
        InitialDelay = TimeSpan.FromMinutes(1),
        MaxDelay = TimeSpan.FromMinutes(1),
        JitterFactor = 0
    };

    private readonly string _connectionString =
        $"Data Source=SpindleSqliteRetryTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly FakeSpindleClock _clock = new(DateTimeOffset.Parse("2026-10-28T12:00:00Z"));
    private SqliteConnection _databaseAnchor = null!;
    private int _executions;

    public async Task InitializeAsync()
    {
        _databaseAnchor = new SqliteConnection(_connectionString);
        await _databaseAnchor.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _databaseAnchor.DisposeAsync();
    }

    [Fact]
    public async Task PendingRetry_SurvivesRuntimeAndStoreRecreation()
    {
        FlowInstanceId instanceId;

        await using (var first = await CreateRuntimeAsync(failures: 1))
        {
            using var cancellation = new CancellationTokenSource();
            var run = first.Runtime
                .RunAsync<int, int>(FlowName, 0, Options, cancellation.Token)
                .AsTask();
            await WaitUntilAsync(() => _clock.ActiveTimerCount > 0);

            // Stop the process while it waits for the retry.
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

            instanceId = (await first.Store.FlowInstances.GetByIdempotencyKeyAsync(FlowName, Options.IdempotencyKey!))!.InstanceId;
            var waiting = await first.Store.Nodes.GetAsync(instanceId, StepId);
            Assert.Equal(NodeStatus.Waiting, waiting?.Status);
            Assert.Equal(_clock.UtcNow.AddMinutes(1), waiting?.RetryAt);
        }

        await using var second = await CreateRuntimeAsync(failures: 1);
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(2, await second.Runtime.RunAsync<int, int>(FlowName, 0, Options));

        var node = await second.Store.Nodes.GetAsync(instanceId, StepId);
        var attempts = await second.Store.Nodes.GetAttemptsAsync(instanceId, StepId);
        Assert.Equal(OneMinuteRetry, node?.RetryPolicy);
        Assert.Equal(2, node?.Attempt);
        Assert.Equal([NodeStatus.Failed, NodeStatus.Completed], attempts.Select(attempt => attempt.Status));
    }

    [Fact]
    public async Task RetryAsync_RecoversFailedFlowAndReopensBarrier()
    {
        await using var application = await CreateRuntimeAsync(failures: 2, retry: RetryPolicy.None, withBarrier: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.Runtime.RunAsync<int, int>(FlowName, 0, Options).AsTask());
        var instanceId = (await application.Store.FlowInstances.GetByIdempotencyKeyAsync(FlowName, Options.IdempotencyKey!))!.InstanceId;
        Assert.Equal(NodeStatus.Failed, (await application.Store.Nodes.GetAsync(instanceId, new NodeId("barrier")))?.Status);

        await application.Runtime.RetryAsync(instanceId);
        Assert.Equal(NodeStatus.Waiting, (await application.Store.Nodes.GetAsync(instanceId, new NodeId("barrier")))?.Status);

        // The fresh budget of RetryPolicy.None allows one attempt, which fails again.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.Runtime.RunAsync<int, int>(FlowName, 0, Options).AsTask());

        await application.Runtime.RetryAsync(instanceId, StepId);
        Assert.Equal(3, await application.Runtime.RunAsync<int, int>(FlowName, 0, Options));

        var attempts = await application.Store.Nodes.GetAttemptsAsync(instanceId, StepId);
        Assert.Equal(
            [NodeStatus.Failed, NodeStatus.Failed, NodeStatus.Completed],
            attempts.Select(attempt => attempt.Status));
        Assert.Equal(FlowInstanceStatus.Completed, (await application.Store.FlowInstances.GetAsync(instanceId))?.Status);
    }

    private async Task<SqliteRuntime> CreateRuntimeAsync(
        int failures,
        RetryPolicy? retry = null,
        bool withBarrier = false)
    {
        var services = new ServiceCollection();
        services.AddSpindleSqlite(_connectionString);
        var provider = services.BuildServiceProvider();

        var contextFactory = provider.GetRequiredService<IDbContextFactory<SpindleDbContext>>();
        await using (var database = await contextFactory.CreateDbContextAsync())
        {
            await database.Database.MigrateAsync();
        }

        var store = provider.GetRequiredService<ISpindleStore>();
        var runtime = new RuntimeSpindleRuntime(store, options: new RuntimeSpindleOptions { TimeProvider = _clock });
        runtime.RegisterFlow<int, int>(
            FlowName,
            async (context, _) =>
            {
                var step = context
                    .Step<int>("flaky", "Flaky", () => ++_executions <= failures
                        ? throw new InvalidOperationException($"failure {_executions}")
                        : ValueTask.FromResult(_executions))
                    .WithRetry(retry ?? OneMinuteRetry);

                if (withBarrier)
                {
                    await context.WaitAll("barrier", BarrierCompletionMode.SuccessfulOnly, step);
                }

                return await step;
            });

        return new SqliteRuntime(provider, store, runtime);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellation.Token);
        }
    }

    private sealed record SqliteRuntime(
        ServiceProvider Provider,
        ISpindleStore Store,
        RuntimeSpindleRuntime Runtime)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}

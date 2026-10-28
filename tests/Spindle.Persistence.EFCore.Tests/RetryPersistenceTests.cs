using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.Nodes;
using Xunit;

namespace Spindle.Persistence.EFCore.Tests;

public sealed class RetryPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-28T12:00:00Z");
    private static readonly FlowInstanceId InstanceId = new("retry-instance");
    private static readonly NodeId StepId = new("step");
    private static readonly NodeId BarrierId = new("barrier");

    private readonly string _connectionString =
        $"Data Source=SpindleRetryPersistenceTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _databaseAnchor = null!;
    private ServiceProvider _provider = null!;
    private ISpindleStore _store = null!;

    public async Task InitializeAsync()
    {
        _databaseAnchor = new SqliteConnection(_connectionString);
        await _databaseAnchor.OpenAsync();

        var services = new ServiceCollection();
        services.AddSpindleSqlite(_connectionString);
        _provider = services.BuildServiceProvider();
        await _provider.GetRequiredService<SpindleDbContext>().Database.MigrateAsync();
        _store = _provider.GetRequiredService<ISpindleStore>();

        await _store.FlowInstances.CreateAsync(new FlowInstanceRecord
        {
            InstanceId = InstanceId,
            FlowName = new FlowName("retry-flow"),
            FlowVersion = new FlowVersion("1"),
            DefinitionHash = "hash",
            Status = FlowInstanceStatus.Running,
            Input = new SerializedPayload
            {
                ContentType = "application/json",
                TypeName = typeof(object).FullName!,
                Data = []
            },
            CreatedAt = Now,
            UpdatedAt = Now
        });
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _databaseAnchor.DisposeAsync();
    }

    [Fact]
    public async Task Policies_RoundTripAsJsonWithEnumNames()
    {
        var retry = new RetryPolicy
        {
            MaxAttempts = 4,
            InitialDelay = TimeSpan.FromSeconds(3),
            MaxDelay = TimeSpan.FromMinutes(2),
            BackoffKind = RetryBackoffKind.Linear,
            JitterFactor = 0.1
        };
        var timeout = new TimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(30),
            CancellationGracePeriod = TimeSpan.FromSeconds(2),
            Behavior = TimeoutBehavior.Continue
        };

        await _store.Nodes.CreateAsync(Step() with { RetryPolicy = retry, TimeoutPolicy = timeout });
        await _store.Nodes.CreateAsync(Step(new NodeId("plain")));

        var node = await _store.Nodes.GetAsync(InstanceId, StepId);
        var plain = await _store.Nodes.GetAsync(InstanceId, new NodeId("plain"));
        Assert.Equal(retry, node?.RetryPolicy);
        Assert.Equal(timeout, node?.TimeoutPolicy);
        Assert.Null(plain?.RetryPolicy);
        Assert.Null(plain?.TimeoutPolicy);
        Assert.Equal(0, plain?.RetryAttemptBaseline);

        await using var command = _databaseAnchor.CreateCommand();
        command.CommandText = "SELECT RetryPolicy || TimeoutPolicy FROM NodeInstances WHERE NodeId = 'step'";
        var json = (string)(await command.ExecuteScalarAsync())!;
        Assert.Contains("\"Linear\"", json);
        Assert.Contains("\"Continue\"", json);
    }

    [Fact]
    public async Task TryScheduleRetry_OnlyAppliesToTheRunningAttempt()
    {
        await _store.Nodes.CreateAsync(Step());
        await StartAttemptAsync("attempt-1");
        var retryAt = Now.AddMinutes(1);

        Assert.False(await _store.Nodes.TryScheduleRetryAsync(InstanceId, StepId, 2, NodeStatus.Failed, "boom", retryAt, Now));
        Assert.True(await _store.Nodes.TryScheduleRetryAsync(InstanceId, StepId, 1, NodeStatus.TimedOut, "boom", retryAt, Now));
        Assert.False(await _store.Nodes.TryScheduleRetryAsync(InstanceId, StepId, 1, NodeStatus.Failed, "boom", retryAt, Now));

        var node = await _store.Nodes.GetAsync(InstanceId, StepId);
        var attempt = Assert.Single(await _store.Nodes.GetAttemptsAsync(InstanceId, StepId));
        Assert.Equal(NodeStatus.Waiting, node?.Status);
        Assert.Equal(retryAt, node?.RetryAt);
        Assert.Equal("boom", node?.Error);
        Assert.Equal(NodeStatus.TimedOut, attempt.Status);
        Assert.Equal(Now, attempt.CompletedAt);
    }

    [Fact]
    public async Task TryMarkRetryReady_RequiresMatchingRetryTime()
    {
        await _store.Nodes.CreateAsync(Step());
        await StartAttemptAsync("attempt-1");
        var retryAt = Now.AddMinutes(1);
        await _store.Nodes.TryScheduleRetryAsync(InstanceId, StepId, 1, NodeStatus.Failed, "boom", retryAt, Now);

        Assert.False(await _store.Nodes.TryMarkRetryReadyAsync(InstanceId, StepId, Now.AddSeconds(30), retryAt));
        Assert.True(await _store.Nodes.TryMarkRetryReadyAsync(InstanceId, StepId, retryAt, retryAt));
        Assert.False(await _store.Nodes.TryMarkRetryReadyAsync(InstanceId, StepId, retryAt, retryAt));

        var node = await _store.Nodes.GetAsync(InstanceId, StepId);
        Assert.Equal(NodeStatus.Ready, node?.Status);
        Assert.Null(node?.RetryAt);
    }

    [Fact]
    public async Task TryInterrupt_ReturnsStepToReadyWithoutUsingRetryBudget()
    {
        await _store.Nodes.CreateAsync(Step());
        await StartAttemptAsync("attempt-1");

        Assert.True(await _store.Nodes.TryInterruptAsync(InstanceId, StepId, 1, Now));
        Assert.False(await _store.Nodes.TryInterruptAsync(InstanceId, StepId, 1, Now));

        var node = await _store.Nodes.GetAsync(InstanceId, StepId);
        Assert.Equal(NodeStatus.Ready, node?.Status);
        Assert.Equal(1, node?.Attempt);
        Assert.Equal(1, node?.RetryAttemptBaseline);
        Assert.Equal(NodeStatus.Cancelled, Assert.Single(await _store.Nodes.GetAttemptsAsync(InstanceId, StepId)).Status);
    }

    [Fact]
    public async Task TryScheduleRecovery_StartsFreshBudgetForExpectedFailure()
    {
        await _store.Nodes.CreateAsync(Step());
        await StartAttemptAsync("attempt-1");
        await _store.Nodes.MarkFailedAsync(InstanceId, StepId, 1, "boom", Now, retryAt: null);
        var retryAt = Now.AddSeconds(1);

        Assert.False(await _store.Nodes.TryScheduleRecoveryAsync(InstanceId, StepId, NodeStatus.TimedOut, 1, retryAt, Now));
        Assert.False(await _store.Nodes.TryScheduleRecoveryAsync(InstanceId, StepId, NodeStatus.Failed, 2, retryAt, Now));
        Assert.True(await _store.Nodes.TryScheduleRecoveryAsync(InstanceId, StepId, NodeStatus.Failed, 1, retryAt, Now));

        var node = await _store.Nodes.GetAsync(InstanceId, StepId);
        Assert.Equal(NodeStatus.Waiting, node?.Status);
        Assert.Equal(retryAt, node?.RetryAt);
        Assert.Equal(1, node?.RetryAttemptBaseline);
        Assert.Null(node?.CompletedAt);
        Assert.Equal(NodeStatus.Failed, Assert.Single(await _store.Nodes.GetAttemptsAsync(InstanceId, StepId)).Status);
    }

    [Fact]
    public async Task TryReopenBarrier_OnlyReopensFailedBarriers()
    {
        await _store.Nodes.CreateAsync(Step());
        await _store.Nodes.CreateAsync(Step(BarrierId) with
        {
            Kind = NodeKind.WaitAll,
            Status = NodeStatus.Waiting,
            Dependencies = [StepId]
        });
        await _store.Nodes.MarkFailedAsync(InstanceId, StepId, -1, "step failed", Now, retryAt: null);
        await _store.Nodes.MarkFailedAsync(InstanceId, BarrierId, -1, "barrier failed", Now, retryAt: null);

        Assert.False(await _store.Nodes.TryReopenBarrierAsync(InstanceId, StepId, Now));
        Assert.True(await _store.Nodes.TryReopenBarrierAsync(InstanceId, BarrierId, Now));
        Assert.False(await _store.Nodes.TryReopenBarrierAsync(InstanceId, BarrierId, Now));

        var barrier = await _store.Nodes.GetAsync(InstanceId, BarrierId);
        Assert.Equal(NodeStatus.Waiting, barrier?.Status);
        Assert.Null(barrier?.Error);
        Assert.Null(barrier?.CompletedAt);
        Assert.Equal([StepId], barrier?.Dependencies);
    }

    [Fact]
    public async Task TryReopen_OnlyReopensFailedFlows()
    {
        Assert.False(await _store.FlowInstances.TryReopenAsync(InstanceId, Now));

        await _store.FlowInstances.MarkFailedAsync(InstanceId, "boom", Now);
        Assert.True(await _store.FlowInstances.TryReopenAsync(InstanceId, Now.AddMinutes(1)));

        var instance = await _store.FlowInstances.GetAsync(InstanceId);
        Assert.Equal(FlowInstanceStatus.Running, instance?.Status);
        Assert.Null(instance?.Error);
        Assert.Null(instance?.CompletedAt);
        Assert.Equal(Now.AddMinutes(1), instance?.UpdatedAt);
    }

    private ValueTask StartAttemptAsync(string attemptId)
        => _store.Nodes.MarkRunningAsync(InstanceId, StepId, new StepAttemptId(attemptId), "worker", Now);

    private static NodeInstanceRecord Step(NodeId? nodeId = null) => new()
    {
        FlowInstanceId = InstanceId,
        NodeId = nodeId ?? StepId,
        Name = "Step",
        Kind = NodeKind.Step,
        Status = NodeStatus.Ready,
        DispatchMode = StepDispatchMode.LocalWorker,
        CreatedAt = Now,
        UpdatedAt = Now
    };
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Steps;
using Spindle.Persistence.Timers;
using Xunit;

namespace Spindle.Persistence.EFCore.Tests;

public sealed class EntityFrameworkPersistenceTests
{
    [Fact]
    public async Task SqliteProvider_ImplementsPersistenceContracts()
    {
        var connectionString = $"Data Source=SpindlePersistenceTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var databaseAnchor = new SqliteConnection(connectionString);
        await databaseAnchor.OpenAsync();

        var services = new ServiceCollection();
        services.AddSpindleSqlite(connectionString);
        await using var provider = services.BuildServiceProvider();
        var database = provider.GetRequiredService<SpindleDbContext>();
        await database.Database.MigrateAsync();
        var store = provider.GetRequiredService<ISpindleStore>();
        var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var flowName = new FlowName("contract-flow");
        var flowVersion = new FlowVersion("1");
        var instanceId = new FlowInstanceId("instance-1");
        var stepId = new StepId("step-1");
        var payload = new SerializedPayload
        {
            ContentType = "application/json",
            TypeName = typeof(string).FullName!,
            Data = [123, 125]
        };

        await store.FlowDefinitions.UpsertAsync(new FlowDefinitionRecord
        {
            FlowName = flowName,
            FlowVersion = flowVersion,
            DefinitionHash = "hash",
            FlowTypeName = "ContractFlow",
            CreatedAt = now,
            UpdatedAt = now
        });
        Assert.NotNull(await store.FlowDefinitions.GetAsync(flowName, flowVersion));

        await store.FlowInstances.CreateAsync(new FlowInstanceRecord
        {
            InstanceId = instanceId,
            FlowName = flowName,
            FlowVersion = flowVersion,
            DefinitionHash = "hash",
            Status = FlowInstanceStatus.Running,
            Input = payload,
            IdempotencyKey = "idempotency-1",
            CreatedAt = now,
            UpdatedAt = now
        });
        Assert.NotNull(await store.FlowInstances.GetByIdempotencyKeyAsync(flowName, "idempotency-1"));
        Assert.Single(await store.FlowInstances.GetRunnableAsync(10));

        await store.Steps.CreateAsync(new StepInstanceRecord
        {
            FlowInstanceId = instanceId,
            StepId = stepId,
            Name = "Step 1",
            Kind = StepKind.Step,
            Status = StepStatus.Ready,
            DispatchMode = StepDispatchMode.LocalWorker,
            Dependencies = [new StepId("dependency-1")],
            Input = payload,
            CreatedAt = now,
            UpdatedAt = now
        });
        Assert.Single(await store.Steps.GetReadyStepsAsync(1));
        await store.Steps.MarkRunningAsync(instanceId, stepId, new StepAttemptId("attempt-1"), "worker", now);
        await store.Steps.MarkCompletedAsync(instanceId, stepId, payload, now.AddMinutes(1));
        var step = await store.Steps.GetAsync(instanceId, stepId);
        Assert.Equal(1, step?.Attempt);
        Assert.Equal(new StepId("dependency-1"), Assert.Single(step!.Dependencies));

        await store.Timers.CreateAsync(new TimerRecord
        {
            FlowInstanceId = instanceId,
            StepId = stepId,
            DueAt = now.AddMinutes(5),
            CreatedAt = now
        });
        Assert.Single(await store.Timers.GetDueAsync(now.AddMinutes(5), 1));
        await store.Timers.MarkFiredAsync(instanceId, stepId, now.AddMinutes(5));
        Assert.Empty(await store.Timers.GetDueAsync(now.AddMinutes(5), 1));

        await store.Signals.CreateWaitAsync(new SignalWaitRecord
        {
            FlowInstanceId = instanceId,
            StepId = stepId,
            SignalName = new SignalName("continue"),
            CorrelationKey = new CorrelationKey("correlation-1"),
            CreatedAt = now
        });
        Assert.Single(await store.Signals.GetOpenWaitsAsync(
            new SignalName("continue"),
            new CorrelationKey("correlation-1")));
        await store.Signals.MarkWaitCompletedAsync(instanceId, stepId, now.AddMinutes(1));

        await store.Outbox.AddAsync(new OutboxMessageRecord
        {
            MessageId = "outbox-1",
            Kind = "test",
            Payload = payload,
            Headers = new Dictionary<string, string> { ["trace-id"] = "42" },
            CreatedAt = now
        });
        var unpublished = Assert.Single(await store.Outbox.GetUnpublishedAsync(1));
        Assert.Equal("42", unpublished.Headers["trace-id"]);
        await store.Outbox.MarkPublishedAsync("outbox-1", now.AddMinutes(1));
        Assert.Empty(await store.Outbox.GetUnpublishedAsync(1));

        var inbox = new InboxMessageRecord
        {
            MessageId = "inbox-1",
            Kind = "test",
            Payload = payload,
            ReceivedAt = now
        };
        Assert.True(await store.Inbox.TryRecordAsync(inbox));
        Assert.False(await store.Inbox.TryRecordAsync(inbox));
        Assert.NotNull(await store.Inbox.GetAsync("inbox-1"));

        var lease = new StepLeaseRecord
        {
            FlowInstanceId = instanceId,
            StepId = stepId,
            Owner = "worker-1",
            AcquiredAt = now,
            ExpiresAt = now.AddMinutes(1)
        };
        Assert.True(await store.Leases.TryAcquireStepLeaseAsync(lease));
        Assert.False(await store.Leases.TryAcquireStepLeaseAsync(lease with { Owner = "worker-2" }));
        await store.Leases.ReleaseStepLeaseAsync(instanceId, stepId, "worker-1");
        Assert.True(await store.Leases.TryAcquireStepLeaseAsync(lease with { Owner = "worker-2" }));

        await store.History.AppendAsync(new ExecutionHistoryRecord
        {
            FlowInstanceId = instanceId,
            StepId = stepId,
            EventType = "started",
            CreatedAt = now
        });
        await store.History.AppendAsync(new ExecutionHistoryRecord
        {
            FlowInstanceId = instanceId,
            StepId = stepId,
            EventType = "completed",
            CreatedAt = now.AddMinutes(1)
        });
        Assert.Equal(2, (await store.History.GetByFlowInstanceAsync(instanceId)).Count);
    }
}

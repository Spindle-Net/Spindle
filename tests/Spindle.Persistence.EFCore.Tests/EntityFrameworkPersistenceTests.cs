using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.Conditions;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Persistence.FlowDefinitions;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.History;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Nodes;
using Spindle.Persistence.Timers;
using Xunit;

namespace Spindle.Persistence.EFCore.Tests;

public sealed class EntityFrameworkPersistenceTests
{
    [Fact]
    public async Task ConditionWaits_PersistPollingIntervalAndDeadline()
    {
        var connectionString = $"Data Source=SpindleConditionWaitTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var databaseAnchor = new SqliteConnection(connectionString);
        await databaseAnchor.OpenAsync();

        var services = new ServiceCollection();
        services.AddSpindleSqlite(connectionString);
        await using var provider = services.BuildServiceProvider();
        var database = provider.GetRequiredService<SpindleDbContext>();
        await database.Database.MigrateAsync();

        var store = provider.GetRequiredService<ISpindleStore>();
        var now = DateTimeOffset.Parse("2026-08-25T12:00:00Z");
        var instanceId = new FlowInstanceId("condition-instance");
        var nodeId = new NodeId("condition");

        await store.FlowInstances.CreateAsync(new FlowInstanceRecord
        {
            InstanceId = instanceId,
            FlowName = new FlowName("condition-flow"),
            FlowVersion = new FlowVersion("1"),
            DefinitionHash = "hash",
            Status = FlowInstanceStatus.Waiting,
            Input = new SerializedPayload
            {
                ContentType = "application/json",
                TypeName = typeof(object).FullName!,
                Data = []
            },
            CreatedAt = now,
            UpdatedAt = now
        });
        await store.Nodes.CreateAsync(new NodeInstanceRecord
        {
            FlowInstanceId = instanceId,
            NodeId = nodeId,
            Name = "Condition",
            Kind = NodeKind.ConditionWait,
            Status = NodeStatus.Waiting,
            DispatchMode = StepDispatchMode.LocalWorker,
            CreatedAt = now,
            UpdatedAt = now
        });
        await store.Conditions.CreateAsync(new ConditionWaitRecord
        {
            FlowInstanceId = instanceId,
            NodeId = nodeId,
            PollingInterval = TimeSpan.FromMinutes(5),
            ExpiresAt = now.AddDays(31),
            CreatedAt = now
        });

        var persisted = await store.Conditions.GetAsync(instanceId, nodeId);

        Assert.NotNull(persisted);
        Assert.Equal(TimeSpan.FromMinutes(5), persisted.PollingInterval);
        Assert.Equal(now.AddDays(31), persisted.ExpiresAt);
    }

    [Fact]
    public async Task GenericNodesMigration_PreservesExistingNodesAndDependencies()
    {
        var connectionString = $"Data Source=SpindleNodeMigrationTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var databaseAnchor = new SqliteConnection(connectionString);
        await databaseAnchor.OpenAsync();

        var services = new ServiceCollection();
        services.AddSpindleSqlite(connectionString);
        await using var provider = services.BuildServiceProvider();
        var database = provider.GetRequiredService<SpindleDbContext>();
        var migrator = database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260806150452_Make_SignalWait_CorrelationKey_Required");

        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO StepInstances
                (FlowInstanceId, StepId, Name, Kind, Status, DispatchMode, Attempt, CreatedAt, UpdatedAt)
            VALUES
                ('flow', 'parent', 'Parent', 0, 4, 0, 1, 1, 1),
                ('flow', 'child', 'Child', 0, 0, 0, 0, 1, 1);
            INSERT INTO StepDependencies (FlowInstanceId, StepId, DependsOnId)
            VALUES ('flow', 'child', 'parent');
            """);

        await migrator.MigrateAsync();

        await using var command = databaseAnchor.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM NodeInstances n
            JOIN NodeDependencies d
              ON d.FlowInstanceId = n.FlowInstanceId AND d.NodeId = n.NodeId
            WHERE n.FlowInstanceId = 'flow'
              AND n.NodeId = 'child'
              AND n.DependencyMode = 0
              AND d.DependsOnId = 'parent'
              AND d.Position = 0;
            """;

        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task NodeDependencies_PreserveDeclarationOrder()
    {
        var connectionString = $"Data Source=SpindleDependencyOrderTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var databaseAnchor = new SqliteConnection(connectionString);
        await databaseAnchor.OpenAsync();

        var services = new ServiceCollection();
        services.AddSpindleSqlite(connectionString);
        await using var provider = services.BuildServiceProvider();
        var database = provider.GetRequiredService<SpindleDbContext>();
        await database.Database.MigrateAsync();

        var store = provider.GetRequiredService<ISpindleStore>();
        var flowInstanceId = new FlowInstanceId("dependency-order-instance");
        var now = DateTimeOffset.Parse("2026-08-09T12:00:00Z");
        var declaredOrder = new[] { new NodeId("z"), new NodeId("a"), new NodeId("m") };

        await store.Nodes.CreateManyAsync(
        [
            .. declaredOrder.Select(CreateCompletedNode),
            new NodeInstanceRecord
            {
                FlowInstanceId = flowInstanceId,
                NodeId = new NodeId("barrier"),
                Name = "Barrier",
                Kind = NodeKind.WaitAny,
                Status = NodeStatus.Waiting,
                DispatchMode = StepDispatchMode.Immediate,
                Dependencies = declaredOrder,
                CreatedAt = now,
                UpdatedAt = now,
            }
        ]);

        var barrier = await store.Nodes.GetAsync(flowInstanceId, new NodeId("barrier"));

        Assert.Equal(declaredOrder, barrier!.Dependencies);

        NodeInstanceRecord CreateCompletedNode(NodeId nodeId) => new()
        {
            FlowInstanceId = flowInstanceId,
            NodeId = nodeId,
            Name = nodeId.Value,
            Kind = NodeKind.Step,
            Status = NodeStatus.Completed,
            DispatchMode = StepDispatchMode.Immediate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    [Fact]
    public async Task MarkDependentsReadyAsync_UpdatesOnlyDependentsOfChangedSteps()
    {
        var connectionString = $"Data Source=SpindleDependencyTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var databaseAnchor = new SqliteConnection(connectionString);
        await databaseAnchor.OpenAsync();

        var services = new ServiceCollection();
        services.AddSpindleSqlite(connectionString);
        await using var provider = services.BuildServiceProvider();
        var database = provider.GetRequiredService<SpindleDbContext>();
        await database.Database.MigrateAsync();

        var store = provider.GetRequiredService<ISpindleStore>();
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var flowInstanceId = new FlowInstanceId("dependency-instance");
        var completedNodeId = new NodeId("completed");
        var incompleteNodeId = new NodeId("incomplete");
        var dependentNodeId = new NodeId("dependent");
        var unrelatedNodeId = new NodeId("unrelated");

        await store.Nodes.CreateManyAsync(
        [
            CreateNode(completedNodeId, NodeStatus.Completed, []),
            CreateNode(incompleteNodeId, NodeStatus.Pending, []),
            CreateNode(dependentNodeId, NodeStatus.Pending, [completedNodeId]),
            CreateNode(unrelatedNodeId, NodeStatus.Pending, [incompleteNodeId]),
        ]);

        await store.Nodes.MarkDependentsReadyAsync(flowInstanceId, [completedNodeId], now.AddMinutes(1));

        Assert.Equal(NodeStatus.Ready, (await store.Nodes.GetAsync(flowInstanceId, dependentNodeId))!.Status);
        Assert.Equal(now.AddMinutes(1), (await store.Nodes.GetAsync(flowInstanceId, dependentNodeId))!.UpdatedAt);
        Assert.Equal(NodeStatus.Pending, (await store.Nodes.GetAsync(flowInstanceId, unrelatedNodeId))!.Status);

        NodeInstanceRecord CreateNode(NodeId nodeId, NodeStatus status, List<NodeId> dependencies) => new()
        {
            FlowInstanceId = flowInstanceId,
            NodeId = nodeId,
            Name = nodeId.Value,
            Kind = NodeKind.Step,
            Status = status,
            DispatchMode = StepDispatchMode.LocalWorker,
            Dependencies = dependencies,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

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
        await AssertPayloadColumnsAsync(databaseAnchor, "ExecutionHistories", "Payload");
        await AssertPayloadColumnsAsync(databaseAnchor, "FlowDefinitions", "Definition");
        await AssertPayloadColumnsAsync(databaseAnchor, "FlowInstances", "Input", "Result");
        await AssertPayloadColumnsAsync(databaseAnchor, "InboxMessages", "Payload");
        await AssertPayloadColumnsAsync(databaseAnchor, "OutboxMessages", "Payload");
        await AssertPayloadColumnsAsync(databaseAnchor, "Signals", "Payload");
        await AssertPayloadColumnsAsync(databaseAnchor, "NodeInstances", "Input", "Result");
        var store = provider.GetRequiredService<ISpindleStore>();
        var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var flowName = new FlowName("contract-flow");
        var flowVersion = new FlowVersion("1");
        var instanceId = new FlowInstanceId("instance-1");
        var nodeId = new NodeId("step-1");
        var dependencyNodeId = new NodeId("dependency-1");
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

        await store.Nodes.CreateManyAsync(
        [
            new NodeInstanceRecord
            {
                FlowInstanceId = instanceId,
                NodeId = dependencyNodeId,
                Name = "Dependency step",
                Kind = NodeKind.Step,
                Status = NodeStatus.Completed,
                DispatchMode = StepDispatchMode.LocalWorker,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new NodeInstanceRecord
            {
                FlowInstanceId = instanceId,
                NodeId = nodeId,
                Name = "Step 1",
                Kind = NodeKind.Step,
                Status = NodeStatus.Ready,
                DispatchMode = StepDispatchMode.LocalWorker,
                Dependencies = [dependencyNodeId],
                Input = payload,
                CreatedAt = now,
                UpdatedAt = now,
            }
        ]);
        Assert.Single(await store.Nodes.GetReadyNodesAsync(1));
        await store.Nodes.MarkRunningAsync(instanceId, nodeId, new StepAttemptId("attempt-1"), "worker", now);
        await store.Nodes.MarkCompletedAsync(instanceId, nodeId, 1, payload, now.AddMinutes(1));
        var step = await store.Nodes.GetAsync(instanceId, nodeId);
        Assert.Equal(1, step?.Attempt);
        Assert.Equal(now, step?.StartedAt);
        Assert.Equal(dependencyNodeId, Assert.Single(step!.Dependencies));

        await store.Timers.CreateAsync(new TimerRecord
        {
            FlowInstanceId = instanceId,
            NodeId = nodeId,
            DueAt = now.AddMinutes(5),
            CreatedAt = now
        });
        Assert.Single(await store.Timers.GetDueAsync(now.AddMinutes(5), 1));
        await store.Timers.MarkFiredAsync(instanceId, nodeId, now.AddMinutes(5));
        Assert.Empty(await store.Timers.GetDueAsync(now.AddMinutes(5), 1));

        await store.Signals.CreateWaitAsync(new SignalWaitRecord
        {
            FlowInstanceId = instanceId,
            NodeId = nodeId,
            SignalName = new SignalName("continue"),
            CorrelationKey = new CorrelationKey("correlation-1"),
            CreatedAt = now
        });
        Assert.Single(await store.Signals.GetOpenWaitsAsync(
            new SignalName("continue"),
            new CorrelationKey("correlation-1")));
        await store.Signals.MarkWaitCompletedAsync(instanceId, nodeId, now.AddMinutes(1));

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
            NodeId = nodeId,
            Owner = "worker-1",
            AcquiredAt = now,
            ExpiresAt = now.AddMinutes(1)
        };
        Assert.True(await store.Leases.TryAcquireStepLeaseAsync(lease));
        Assert.False(await store.Leases.TryAcquireStepLeaseAsync(lease with { Owner = "worker-2" }));
        await store.Leases.ReleaseStepLeaseAsync(instanceId, nodeId, "worker-1");
        Assert.True(await store.Leases.TryAcquireStepLeaseAsync(lease with { Owner = "worker-2" }));

        await store.History.AppendAsync(new ExecutionHistoryRecord
        {
            FlowInstanceId = instanceId,
            NodeId = nodeId,
            EventType = "started",
            CreatedAt = now
        });
        await store.History.AppendAsync(new ExecutionHistoryRecord
        {
            FlowInstanceId = instanceId,
            NodeId = nodeId,
            EventType = "completed",
            CreatedAt = now.AddMinutes(1)
        });
        Assert.Equal(2, (await store.History.GetByFlowInstanceAsync(instanceId)).Count);
    }

    private static async Task AssertPayloadColumnsAsync(
        SqliteConnection connection,
        string tableName,
        params string[] propertyNames)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}')";

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        foreach (var propertyName in propertyNames)
        {
            Assert.Contains($"{propertyName}_ContentType", columns);
            Assert.Contains($"{propertyName}_TypeName", columns);
            Assert.Contains($"{propertyName}_Data", columns);
            Assert.DoesNotContain(propertyName, columns);
        }
    }
}

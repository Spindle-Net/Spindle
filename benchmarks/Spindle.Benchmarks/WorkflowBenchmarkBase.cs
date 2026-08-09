using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spindle;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.Nodes;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Persistence.InMemory;

namespace Spindle.Benchmarks;

public abstract class WorkflowBenchmarkBase : IDisposable
{
    private RuntimeSpindleRuntime? _runtime;
    private SqliteConnection? _databaseAnchor;
    private ServiceProvider? _serviceProvider;
    private FlowName? _flowName;
    private ISpindleStore? _store;

    [Params(StoreProvider.InMemory, StoreProvider.Sqlite)]
    public StoreProvider Provider { get; set; }

    public void Dispose() => CleanupAsync().GetAwaiter().GetResult();

    protected async Task SetupAsync(Func<IFlowContext, int, ValueTask<int>> flow)
    {
        await CleanupAsync();

        ISpindleStore store;
        if (Provider == StoreProvider.InMemory)
        {
            store = new InMemorySpindleStore();
        }
        else
        {
            var connectionString = $"Data Source=SpindleBenchmarks-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
            _databaseAnchor = new SqliteConnection(connectionString);
            await _databaseAnchor.OpenAsync();

            var services = new ServiceCollection();
            services.AddSpindleSqlite(connectionString);
            _serviceProvider = services.BuildServiceProvider();
            var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<SpindleDbContext>>();
            await using (var database = await contextFactory.CreateDbContextAsync())
            {
                await database.Database.MigrateAsync();
            }

            store = _serviceProvider.GetRequiredService<ISpindleStore>();
        }

        var flowName = new FlowName("benchmark-flow");
        _flowName = flowName;
        _store = store;
        _runtime = new RuntimeSpindleRuntime(store);
        _runtime.RegisterFlow(flowName, flow);
    }

    protected Task CleanupAsync()
    {
        _runtime = null;
        _flowName = null;
        _store = null;

        return DisposeResourcesAsync();
    }

    protected Task RunFlowsAsync(int flowCount)
    {
        return Task.WhenAll(Enumerable.Range(0, flowCount).Select(_ => RunFlowAsync()));
    }

    protected async Task SeedCompletedFlowsAsync(int flowCount, int stepCount)
    {
        var store = _store ?? throw new InvalidOperationException("The benchmark store has not been initialized.");
        var flowName = _flowName ?? throw new InvalidOperationException("The benchmark flow has not been initialized.");
        var now = DateTimeOffset.UnixEpoch;

        await store.ExecuteAsync(
            async (session, cancellationToken) =>
            {
                var steps = new List<NodeInstanceRecord>(flowCount * stepCount);
                for (var flowIndex = 0; flowIndex < flowCount; flowIndex++)
                {
                    var instanceId = new FlowInstanceId($"seed-{flowIndex:D8}");
                    await session.FlowInstances.CreateAsync(
                        new FlowInstanceRecord
                        {
                            InstanceId = instanceId,
                            FlowName = flowName,
                            FlowVersion = new FlowVersion("1"),
                            DefinitionHash = "benchmark-seed",
                            Status = FlowInstanceStatus.Completed,
                            Input = CreateSeedPayload(),
                            Result = CreateSeedPayload(),
                            CreatedAt = now,
                            CompletedAt = now,
                            UpdatedAt = now,
                        },
                        cancellationToken);

                    for (var stepIndex = 0; stepIndex < stepCount; stepIndex++)
                    {
                        steps.Add(new NodeInstanceRecord
                        {
                            FlowInstanceId = instanceId,
                            NodeId = new NodeId($"step-{stepIndex:D4}"),
                            Name = "Step",
                            Kind = NodeKind.Step,
                            Status = NodeStatus.Completed,
                            Dependencies = stepIndex == 0 ? [] : [new NodeId($"step-{stepIndex - 1:D4}")],
                            Result = CreateSeedPayload(),
                            Attempt = 1,
                            StartedAt = now,
                            CompletedAt = now,
                            CreatedAt = now,
                            UpdatedAt = now,
                        });
                    }
                }

                if (steps.Count > 0)
                {
                    await session.Nodes.CreateManyAsync(steps, cancellationToken);
                }
            });
    }

    private static SerializedPayload CreateSeedPayload()
    {
        return new SerializedPayload
        {
            ContentType = "application/json",
            TypeName = "System.Int32",
            Data = "0"u8.ToArray(),
        };
    }

    private Task RunFlowAsync()
    {
        var runtime = _runtime ?? throw new InvalidOperationException("The benchmark runtime has not been initialized.");
        var flowName = _flowName ?? throw new InvalidOperationException("The benchmark flow has not been initialized.");
        return runtime.RunAsync<int, int>(flowName, 0).AsTask();
    }

    private async Task DisposeResourcesAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
            _serviceProvider = null;
        }

        if (_databaseAnchor is not null)
        {
            await _databaseAnchor.DisposeAsync();
            _databaseAnchor = null;
        }
    }
}

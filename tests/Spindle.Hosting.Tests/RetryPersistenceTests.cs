using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Snapshot;
using Spindle.Hosting;
using Spindle.Persistence;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Testing;
using Xunit;

namespace Spindle.Hosting.Tests;

public sealed class RetryPersistenceTests
{
    [Fact]
    public async Task RestartResumesDurableRetryAndManualRecoveryPreservesAttemptHistory()
    {
        var connectionString = $"Data Source=retry-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var clock = new FakeSpindleClock(DateTimeOffset.Parse("2026-10-28T12:00:00Z"));
        var name = new FlowName("durable-retry");
        var stepId = new NodeId("work");
        var calls = 0;
        var goodCalls = 0;
        var policy = RetryPolicy.Exponential(2) with { JitterFactor = 0 };
        var timeout = new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(15) };
        async ValueTask<int> Flow(IFlowContext ctx, int request)
        {
            var good = ctx.Step("good", "Good", () => ValueTask.FromResult(++goodCalls));
            var work = ctx.Step<int>("work", "Work", () => ++calls < 3
                ? throw new NotImplementedException("retry") : ValueTask.FromResult(42))
                .WithRetry(policy).WithTimeout(timeout);
            await ctx.WaitAll("all", Spindle.Abstractions.Nodes.BarrierCompletionMode.SuccessfulOnly, good, work);
            return await work;
        }
        ServiceProvider Provider() => new ServiceCollection().AddSpindleSqlite(connectionString).BuildServiceProvider();
        FlowInstanceId id;
        await using (var provider = Provider())
        {
            await provider.GetRequiredService<SpindleDbContext>().Database.MigrateAsync();
            var store = provider.GetRequiredService<ISpindleStore>();
            var runtime = new RuntimeSpindleRuntime(store, options: new RuntimeSpindleOptions { TimeProvider = clock });
            runtime.RegisterFlow<int, int>(name, Flow);
            id = (await runtime.StartAsync<int, int>(name, 0)).InstanceId;
            var pump = new SpindleRuntimePump(runtime, store, Options.Create(new SpindleHostOptions()));
            await pump.RunUntilIdleAsync();
            var node = await store.Nodes.GetAsync(id, stepId);
            Assert.Equal(NodeStatus.Waiting, node!.Status);
            Assert.Equal(policy, node.Retry);
            Assert.Equal(timeout, node.Timeout);
            Assert.Equal(1, calls);
        }
        await using (var provider = Provider())
        {
            var store = provider.GetRequiredService<ISpindleStore>();
            var runtime = new RuntimeSpindleRuntime(store, options: new RuntimeSpindleOptions { TimeProvider = clock });
            runtime.RegisterFlow<int, int>(name, Flow);
            var pump = new SpindleRuntimePump(runtime, store, Options.Create(new SpindleHostOptions()));
            await pump.RunUntilIdleAsync(); // Becomes quiescent before the retry timer fires.
            Assert.Equal(1, calls);
            clock.AdvanceBy(TimeSpan.FromSeconds(1));
            await pump.RunUntilIdleAsync();
            Assert.Equal(FlowInstanceStatus.Failed, (await runtime.GetInstanceAsync(id))!.Status);
            Assert.Equal(2, calls);
            await runtime.RetryAsync(id, stepId);
            await pump.RunUntilIdleAsync();
            Assert.Equal(FlowInstanceStatus.Completed, (await runtime.GetInstanceAsync(id))!.Status);
            var node = await store.Nodes.GetAsync(id, stepId);
            Assert.Equal(3, node!.Attempt);
            Assert.Equal(2, node.RetryAttemptBaseline);
            Assert.Null(node.RetryAt);
            Assert.Equal(1, goodCalls);
        }
        await using var command = anchor.CreateCommand();
        command.CommandText = "SELECT Status FROM StepAttempts WHERE NodeId = 'work' ORDER BY Attempt";
        await using var reader = await command.ExecuteReaderAsync();
        var outcomes = new List<NodeStatus>();
        while (await reader.ReadAsync()) outcomes.Add((NodeStatus)reader.GetInt32(0));
        Assert.Equal(new[] { NodeStatus.Failed, NodeStatus.Failed, NodeStatus.Completed }, outcomes);
    }
}

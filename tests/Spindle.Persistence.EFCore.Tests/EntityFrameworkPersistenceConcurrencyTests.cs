using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Persistence.Leases;
using Spindle.Persistence.Messaging;
using Xunit;

namespace Spindle.Persistence.EFCore.Tests;

public sealed class EntityFrameworkPersistenceConcurrencyTests
{
    [Fact]
    public Task Inbox_AllowsOnlyOneConcurrentInsert()
    {
        return RunWithStoreAsync(async store =>
        {
            var message = new InboxMessageRecord
            {
                MessageId = "concurrent-message",
                Kind = "test",
                Payload = new SerializedPayload
                {
                    ContentType = "application/json",
                    TypeName = typeof(string).FullName!,
                    Data = [123, 125]
                },
                ReceivedAt = DateTimeOffset.Parse("2026-07-29T12:00:00Z")
            };

            var results = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => store.Inbox.TryRecordAsync(message).AsTask()));

            Assert.Single(results, recorded => recorded);
            Assert.Equal(7, results.Count(recorded => !recorded));
        });
    }

    [Fact]
    public Task Leases_AllowOnlyOneConcurrentOwner()
    {
        return RunWithStoreAsync(async store =>
        {
            var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            var leases = Enumerable.Range(0, 8)
                .Select(index => new StepLeaseRecord
                {
                    FlowInstanceId = new FlowInstanceId("concurrent-instance"),
                    NodeId = new NodeId("concurrent-step"),
                    Owner = $"worker-{index}",
                    AcquiredAt = now,
                    ExpiresAt = now.AddMinutes(1)
                })
                .ToArray();

            var results = await Task.WhenAll(
                leases.Select(lease => store.Leases.TryAcquireStepLeaseAsync(lease).AsTask()));

            Assert.Single(results, acquired => acquired);
            Assert.Equal(7, results.Count(acquired => !acquired));
        });
    }

    [Fact]
    public Task Leases_AllowOnlyOneConcurrentOwnerToReplaceExpiredLease()
    {
        return RunWithStoreAsync(async store =>
        {
            var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            Assert.True(await store.Leases.TryAcquireStepLeaseAsync(new StepLeaseRecord
            {
                FlowInstanceId = new FlowInstanceId("expired-instance"),
                NodeId = new NodeId("expired-step"),
                Owner = "expired-owner",
                AcquiredAt = now.AddMinutes(-2),
                ExpiresAt = now.AddMinutes(-1)
            }));

            var leases = Enumerable.Range(0, 8)
                .Select(index => new StepLeaseRecord
                {
                    FlowInstanceId = new FlowInstanceId("expired-instance"),
                    NodeId = new NodeId("expired-step"),
                    Owner = $"worker-{index}",
                    AcquiredAt = now,
                    ExpiresAt = now.AddMinutes(1)
                })
                .ToArray();

            var results = await Task.WhenAll(
                leases.Select(lease => store.Leases.TryAcquireStepLeaseAsync(lease).AsTask()));

            Assert.Single(results, acquired => acquired);
            Assert.Equal(7, results.Count(acquired => !acquired));
        });
    }

    private static async Task RunWithStoreAsync(Func<ISpindleStore, Task> test)
    {
        var connectionString =
            $"Data Source=SpindlePersistenceConcurrencyTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
        await using var databaseAnchor = new SqliteConnection(connectionString);
        await databaseAnchor.OpenAsync();

        var services = new ServiceCollection();
        services.AddSpindleSqlite(connectionString);
        await using var provider = services.BuildServiceProvider();
        var database = provider.GetRequiredService<SpindleDbContext>();
        await database.Database.MigrateAsync();

        await test(provider.GetRequiredService<ISpindleStore>());
    }
}

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.FlowInstances;
using Spindle.Testing;
using Spindle.Transport;
using Xunit;
using InMemorySpindleStore = Spindle.Persistence.InMemory.InMemorySpindleStore;
using InMemorySpindleTransport = Spindle.Transport.InMemory.InMemorySpindleTransport;

namespace Spindle.Testing.Tests;

public sealed class TestingInfrastructureTests
{
    [Fact]
    public void FakeClock_AdvancesDeterministically()
    {
        var initial = DateTimeOffset.Parse("2026-06-28T12:00:00Z");
        var clock = new FakeSpindleClock(initial);

        var advanced = clock.AdvanceBy(TimeSpan.FromMinutes(5));

        Assert.Equal(initial.AddMinutes(5), advanced);
        Assert.Equal(advanced, clock.UtcNow);
    }

    [Fact]
    public async Task InMemoryFlowStore_CopiesPayloadBytes()
    {
        var store = new InMemorySpindleStore();
        var bytes = new byte[] { 1, 2, 3 };
        var now = DateTimeOffset.Parse("2026-06-28T12:00:00Z");

        await store.ExecuteAsync(
            (session, cancellationToken) =>
                session.FlowInstances.CreateAsync(new FlowInstanceRecord
                {
                    InstanceId = new FlowInstanceId("instance-1"),
                    FlowName = new FlowName("flow"),
                    FlowVersion = new FlowVersion("1"),
                    DefinitionHash = "hash",
                    Status = FlowInstanceStatus.Running,
                    Input = new SerializedPayload
                    {
                        ContentType = "application/json",
                        TypeName = typeof(int).FullName!,
                        Data = bytes
                    },
                    CreatedAt = now,
                    UpdatedAt = now
                }, cancellationToken));

        bytes[0] = 9;

        var loaded = await store.FlowInstances.GetAsync(new FlowInstanceId("instance-1"));

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.Input.Data[0]);
    }

    [Fact]
    public async Task InMemoryStore_ExecutesOperationThroughSingleSession()
    {
        var store = new InMemorySpindleStore();
        var now = DateTimeOffset.Parse("2026-06-28T12:00:00Z");

        var loaded = await store.ExecuteAsync(
            async (session, cancellationToken) =>
            {
                await session.FlowInstances
                    .CreateAsync(new FlowInstanceRecord
                    {
                        InstanceId = new FlowInstanceId("instance-2"),
                        FlowName = new FlowName("flow"),
                        FlowVersion = new FlowVersion("1"),
                        DefinitionHash = "hash",
                        Status = FlowInstanceStatus.Running,
                        Input = new SerializedPayload
                        {
                            ContentType = "application/json",
                            TypeName = typeof(int).FullName!,
                            Data = [1]
                        },
                        CreatedAt = now,
                        UpdatedAt = now
                    }, cancellationToken)
                    .ConfigureAwait(false);

                return await session.FlowInstances
                    .GetAsync(new FlowInstanceId("instance-2"), cancellationToken)
                    .ConfigureAwait(false);
            });

        Assert.NotNull(loaded);
        Assert.Equal(new FlowInstanceId("instance-2"), loaded.InstanceId);
    }

    [Fact]
    public async Task InMemoryTransport_DeliversPublishedMessagesToSubscriber()
    {
        var transport = new InMemorySpindleTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var enumerator = transport.SubscribeAsync(
                new SpindleSubscription
                {
                    Application = new ApplicationName("target"),
                    MessageKinds = new HashSet<SpindleMessageKind> { SpindleMessageKind.StepReady }
                },
                cts.Token)
            .GetAsyncEnumerator(cts.Token);

        try
        {
            var moveNext = enumerator.MoveNextAsync().AsTask();

            await transport.PublishAsync(
                new SpindleMessage
                {
                    MessageId = new SpindleMessageId("message-1"),
                    Kind = SpindleMessageKind.StepReady,
                    SourceApplication = new ApplicationName("source"),
                    TargetApplication = new ApplicationName("target"),
                    Payload = new SerializedPayload
                    {
                        ContentType = "application/json",
                        TypeName = typeof(string).FullName!,
                        Data = []
                    }
                },
                cts.Token);

            Assert.True(await moveNext.WaitAsync(cts.Token));
            Assert.Equal(new SpindleMessageId("message-1"), enumerator.Current.MessageId);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

}

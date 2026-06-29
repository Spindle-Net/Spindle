using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Transport;
using Spindle.Transport.Messages;
using Xunit;

namespace Spindle.Transport.Abstractions.Tests;

public sealed class TransportContractTests
{
    [Fact]
    public void SpindleMessage_CarriesRoutingAndPayloadMetadata()
    {
        var message = new SpindleMessage
        {
            MessageId = new SpindleMessageId("message-1"),
            Kind = SpindleMessageKind.ExecuteStep,
            SourceApplication = new ApplicationName("app-a"),
            TargetApplication = new ApplicationName("app-b"),
            Queue = new QueueName("local"),
            FlowInstanceId = new FlowInstanceId("flow-1"),
            StepId = new StepId("step-1"),
            AttemptId = new StepAttemptId("attempt-1"),
            Payload = new SerializedPayload
            {
                ContentType = "application/json",
                TypeName = typeof(ExecuteStepMessage).FullName!,
                Data = []
            }
        };

        Assert.Equal(new SpindleMessageId("message-1"), message.MessageId);
        Assert.Equal(SpindleMessageKind.ExecuteStep, message.Kind);
        Assert.Equal(new QueueName("local"), message.Queue);
    }

    [Fact]
    public void ExecuteStepMessage_IsImmutableContractPayload()
    {
        var requestedAt = DateTimeOffset.Parse("2026-06-28T12:00:00Z");

        var message = new ExecuteStepMessage
        {
            FlowInstanceId = new FlowInstanceId("flow-1"),
            StepId = new StepId("step-1"),
            AttemptId = new StepAttemptId("attempt-1"),
            Attempt = 1,
            RequestedAt = requestedAt
        };

        Assert.Equal(1, message.Attempt);
        Assert.Equal(requestedAt, message.RequestedAt);
    }
}

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.FlowInstances;
using Spindle.Persistence.Steps;
using Xunit;

namespace Spindle.Persistence.Abstractions.Tests;

public sealed class PersistenceContractTests
{
    [Fact]
    public void FlowInstanceRecord_CarriesRequiredMvpState()
    {
        var now = DateTimeOffset.Parse("2026-06-28T12:00:00Z");
        var payload = new SerializedPayload
        {
            ContentType = "application/json",
            TypeName = typeof(string).FullName!,
            Data = [123, 125]
        };

        var record = new FlowInstanceRecord
        {
            InstanceId = new FlowInstanceId("instance-1"),
            FlowName = new FlowName("flow"),
            FlowVersion = new FlowVersion("1"),
            DefinitionHash = "hash",
            Status = FlowInstanceStatus.Running,
            Input = payload,
            CreatedAt = now,
            UpdatedAt = now
        };

        Assert.Equal(new FlowInstanceId("instance-1"), record.InstanceId);
        Assert.Equal(new FlowName("flow"), record.FlowName);
        Assert.Equal(FlowInstanceStatus.Running, record.Status);
        Assert.Same(payload, record.Input);
    }

    [Fact]
    public void StepInstanceRecord_DefaultsDependenciesToEmpty()
    {
        var step = new StepInstanceRecord
        {
            FlowInstanceId = new FlowInstanceId("instance-1"),
            StepId = new StepId("step-1"),
            Name = "Step 1",
            Kind = StepKind.Step,
            Status = StepStatus.Ready,
            DispatchMode = StepDispatchMode.LocalWorker
        };

        Assert.Empty(step.Dependencies);
        Assert.Null(step.Result);
    }
}

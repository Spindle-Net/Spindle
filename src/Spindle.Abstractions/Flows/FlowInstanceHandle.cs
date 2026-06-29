using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Flows;

public sealed record FlowInstanceHandle<TResult>
{
    public required FlowInstanceId InstanceId { get; init; }

    public required FlowName FlowName { get; init; }

    public required FlowVersion FlowVersion { get; init; }
}
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;

namespace Spindle.Runtime.Nodes;

internal sealed class RuntimeForkNode<TValue>(
    NodeId id, string name,
    Task<TValue> descriptorTask) : ForkNode<TValue>, IRuntimeNode
{
    public override NodeId Id => id;

    public override string Name => name;

    public override NodeKind Kind => NodeKind.Fork;

    public Type ResultType => throw new NotImplementedException();

    public FlowInstanceId FlowInstanceId => throw new NotImplementedException();

    public override async ValueTask<TValue> GetResultAsync(CancellationToken cancellationToken = default)
        => await descriptorTask;
}

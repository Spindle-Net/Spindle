using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;

namespace Spindle;

internal sealed class RuntimeDelayNode(RuntimeNodeState<Unit> state)
    : DelayNode, IRuntimeNode
{
    public override NodeId Id => state.Id;

    public override string Name => state.Name;

    public override NodeKind Kind => state.Kind;

    public Type ResultType => typeof(Unit);

    public FlowInstanceId FlowInstanceId => state.FlowInstanceId;

    public override ValueTask<Unit> GetResultAsync(CancellationToken cancellationToken = default)
        => state.GetResultAsync(cancellationToken);
}

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;

namespace Spindle;

internal sealed class RuntimeWaitAnyNode(
    RuntimeNodeState<WaitAnyResult> state,
    IReadOnlyList<Node> inputs,
    BarrierCompletionMode completionMode)
    : WaitAnyNode, IRuntimeNode
{
    public override NodeId Id => state.Id;

    public override string Name => state.Name;

    public override NodeKind Kind => state.Kind;

    public override IReadOnlyList<Node> Inputs => inputs;

    public override BarrierCompletionMode CompletionMode => completionMode;

    public Type ResultType => typeof(WaitAnyResult);

    public FlowInstanceId FlowInstanceId => state.FlowInstanceId;

    public override ValueTask<WaitAnyResult> GetResultAsync(CancellationToken cancellationToken = default)
        => state.GetResultAsync(cancellationToken);
}

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;

namespace Spindle;

internal sealed class RuntimeWaitAllNode(
    RuntimeNodeState<WaitAllResult> state,
    IReadOnlyList<Node> inputs,
    BarrierCompletionMode completionMode)
    : WaitAllNode, IRuntimeNode
{
    public override NodeId Id => state.Id;

    public override string Name => state.Name;

    public override NodeKind Kind => state.Kind;

    public override IReadOnlyList<Node> Inputs => inputs;

    public override BarrierCompletionMode CompletionMode => completionMode;

    public Type ResultType => typeof(WaitAllResult);

    public FlowInstanceId FlowInstanceId => state.FlowInstanceId;

    public override ValueTask<WaitAllResult> GetResultAsync(CancellationToken cancellationToken = default)
        => state.GetResultAsync(cancellationToken);
}

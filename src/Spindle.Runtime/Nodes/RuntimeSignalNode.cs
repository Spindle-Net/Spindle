using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;

namespace Spindle;

internal sealed class RuntimeSignalNode<TSignal>(
    RuntimeNodeState<TSignal?> state,
    SignalName signalName,
    CorrelationKey correlationKey)
    : SignalNode<TSignal>, IRuntimeNode
{
    public override NodeId Id => state.Id;

    public override string Name => state.Name;

    public override NodeKind Kind => state.Kind;

    public override SignalName SignalName => signalName;

    public override CorrelationKey CorrelationKey => correlationKey;

    public Type ResultType => typeof(TSignal);

    public FlowInstanceId FlowInstanceId => state.FlowInstanceId;

    public override ValueTask<TSignal?> GetResultAsync(CancellationToken cancellationToken = default)
        => state.GetResultAsync(cancellationToken);
}

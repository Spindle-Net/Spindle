using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle;

internal sealed class RuntimeStepNode<TResult>(
    RuntimeNodeState<TResult> state,
    StepOptions options)
    : StepNode<TResult>, IRuntimeNode
{
    public override NodeId Id => state.Id;

    public override string Name => state.Name;

    public override NodeKind Kind => state.Kind;

    public override StepOptions Options => options;

    public Type ResultType => typeof(TResult);

    public FlowInstanceId FlowInstanceId => state.FlowInstanceId;

    public override ValueTask<TResult> GetResultAsync(CancellationToken cancellationToken = default)
        => state.GetResultAsync(cancellationToken);

    public override StepNode<TResult> WithOptions(Func<StepOptions, StepOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return new RuntimeStepNode<TResult>(state, configure(options));
    }
}

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;

namespace Spindle;

internal sealed class RuntimeConditionNode(
    RuntimeNodeState<Unit> state,
    TimeSpan pollingInterval,
    TimeSpan? timeout,
    Func<TimeSpan, bool> configureTimeout)
    : ConditionNode, IRuntimeNode
{
    public override NodeId Id => state.Id;

    public override string Name => state.Name;

    public override NodeKind Kind => state.Kind;

    public override TimeSpan PollingInterval => pollingInterval;

    public override TimeSpan? Timeout => timeout;

    public Type ResultType => typeof(Unit);

    public FlowInstanceId FlowInstanceId => state.FlowInstanceId;

    public override ValueTask<Unit> GetResultAsync(CancellationToken cancellationToken = default)
        => state.GetResultAsync(cancellationToken);

    public override ConditionNode WithTimeout(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        configureTimeout(value);
        return new RuntimeConditionNode(state, pollingInterval, value, configureTimeout);
    }
}

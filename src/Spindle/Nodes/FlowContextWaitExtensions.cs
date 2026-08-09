using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Waiting;

namespace Spindle;

public static class FlowContextWaitExtensions
{
    public static SignalNode<Unit> WaitForSignal(
        this IFlowContext context,
        string id,
        SignalName signalName,
        CorrelationKey correlationKey,
        SignalWaitOptions? options = null)
        => context.WaitForSignal<Unit>(id, signalName, correlationKey, options);

    public static SignalNode<Unit> WaitForSignal(
        this IFlowContext context,
        string id,
        string name,
        SignalName signalName,
        CorrelationKey correlationKey,
        SignalWaitOptions? options = null)
        => context.WaitForSignal<Unit>(id, name, signalName, correlationKey, options);
}

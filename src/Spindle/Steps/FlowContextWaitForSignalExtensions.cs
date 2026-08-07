using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Waiting;

namespace Spindle;

public static class FlowContextWaitForSignalExtensions
{
    public static async ValueTask WaitForSignal(
        this IFlowContext context,
        SignalName signalName,
        CorrelationKey correlationKey,
        SignalWaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await context.WaitForSignal<Unit>(
            signalName,
            correlationKey,
            options,
            cancellationToken);
    }
}

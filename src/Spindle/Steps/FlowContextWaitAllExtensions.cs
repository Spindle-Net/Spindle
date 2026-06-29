using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Steps;

namespace Spindle;

public static class FlowContextWaitAllExtensions
{
    public static ValueTask WaitAll<T1, T2>(
        this IFlowContext context,
        Step<T1> step1,
        Step<T2> step2)
    {
        return context.WaitAll(step1, step2);
    }
}

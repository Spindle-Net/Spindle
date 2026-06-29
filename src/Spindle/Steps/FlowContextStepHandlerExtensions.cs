using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Steps;

namespace Spindle;

public static class FlowContextStepHandlerExtensions
{
    public static Step<TResult> StepHandler<T1, TRequest, TResult>(
        this IFlowContext context,
        string id,
        string name,
        StepHandlerId handlerId,
        Step<T1> input1,
        Func<T1, TRequest> createRequest,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(createRequest);

        return context.StepHandler<TRequest, TResult>(
            id,
            name,
            handlerId,
            [input1],
            inputs => createRequest(inputs.Get<T1>(0)),
            options);
    }

    public static Step<TResult> StepHandler<T1, T2, TRequest, TResult>(
        this IFlowContext context,
        string id,
        string name,
        StepHandlerId handlerId,
        Step<T1> input1,
        Step<T2> input2,
        Func<T1, T2, TRequest> createRequest,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(createRequest);

        return context.StepHandler<TRequest, TResult>(
            id,
            name,
            handlerId,
            [input1, input2],
            inputs => createRequest(inputs.Get<T1>(0), inputs.Get<T2>(1)),
            options);
    }
}

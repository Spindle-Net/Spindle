using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Steps;

namespace Spindle;

public static class FlowContextStepExtensions
{
    public static Step<TResult> Step<TResult>(
        this IFlowContext context,
        string id,
        string name,
        Func<ValueTask<TResult>> execute,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        return context.Step(
            id,
            name,
            [],
            (_, _) => execute(),
            options);
    }

    public static Step<TResult> Step<TResult>(
        this IFlowContext context,
        string id,
        string name,
        Func<IStepExecutionContext, ValueTask<TResult>> execute,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        return context.Step(
            id,
            name,
            [],
            (_, stepContext) => execute(stepContext),
            options);
    }

    public static Step<TResult> Step<T1, TResult>(
        this IFlowContext context,
        string id,
        string name,
        Step<T1> input1,
        Func<T1, ValueTask<TResult>> execute,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        return context.Step(
            id,
            name,
            [input1],
            (inputs, _) => execute(inputs.Get<T1>(0)),
            options);
    }

    public static Step<TResult> Step<T1, TResult>(
        this IFlowContext context,
        string id,
        string name,
        Step<T1> input1,
        Func<T1, IStepExecutionContext, ValueTask<TResult>> execute,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        return context.Step(
            id,
            name,
            [input1],
            (inputs, stepContext) => execute(inputs.Get<T1>(0), stepContext),
            options);
    }

    public static Step<TResult> Step<T1, T2, TResult>(
        this IFlowContext context,
        string id,
        string name,
        Step<T1> input1,
        Step<T2> input2,
        Func<T1, T2, ValueTask<TResult>> execute,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        return context.Step(
            id,
            name,
            [input1, input2],
            (inputs, _) => execute(inputs.Get<T1>(0), inputs.Get<T2>(1)),
            options);
    }

    public static Step<TResult> Step<T1, T2, TResult>(
        this IFlowContext context,
        string id,
        string name,
        Step<T1> input1,
        Step<T2> input2,
        Func<T1, T2, IStepExecutionContext, ValueTask<TResult>> execute,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        return context.Step(
            id,
            name,
            [input1, input2],
            (inputs, stepContext) => execute(inputs.Get<T1>(0), inputs.Get<T2>(1), stepContext),
            options);
    }
}

using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle;

/// <summary>
/// Provides typed convenience overloads for declaring durable condition waits.
/// </summary>
public static class FlowContextConditionExtensions
{
    /// <summary>Declares an unnamed condition without node inputs.</summary>
    public static ConditionNode WaitForCondition(
        this IFlowContext context,
        string id,
        TimeSpan pollingInterval,
        Func<ValueTask<bool>> condition)
        => WaitForCondition(context, id, id, pollingInterval, condition);

    /// <summary>Declares a named condition without node inputs.</summary>
    public static ConditionNode WaitForCondition(
        this IFlowContext context,
        string id,
        string name,
        TimeSpan pollingInterval,
        Func<ValueTask<bool>> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return context.WaitForCondition(
            id,
            name,
            pollingInterval,
            [],
            (_, _) => condition());
    }

    /// <summary>Declares an unnamed condition that receives an execution context.</summary>
    public static ConditionNode WaitForCondition(
        this IFlowContext context,
        string id,
        TimeSpan pollingInterval,
        Func<IStepExecutionContext, ValueTask<bool>> condition)
        => WaitForCondition(context, id, id, pollingInterval, condition);

    /// <summary>Declares a named condition that receives an execution context.</summary>
    public static ConditionNode WaitForCondition(
        this IFlowContext context,
        string id,
        string name,
        TimeSpan pollingInterval,
        Func<IStepExecutionContext, ValueTask<bool>> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return context.WaitForCondition(
            id,
            name,
            pollingInterval,
            [],
            (_, executionContext) => condition(executionContext));
    }

    /// <summary>Declares an unnamed condition with one typed node input.</summary>
    public static ConditionNode WaitForCondition<T1>(
        this IFlowContext context,
        string id,
        TimeSpan pollingInterval,
        Node<T1> input1,
        Func<T1, ValueTask<bool>> condition)
        => WaitForCondition(context, id, id, pollingInterval, input1, condition);

    /// <summary>Declares a named condition with one typed node input.</summary>
    public static ConditionNode WaitForCondition<T1>(
        this IFlowContext context,
        string id,
        string name,
        TimeSpan pollingInterval,
        Node<T1> input1,
        Func<T1, ValueTask<bool>> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return context.WaitForCondition(
            id,
            name,
            pollingInterval,
            [input1],
            (inputs, _) => condition(inputs.Get<T1>(0)));
    }

    /// <summary>Declares an unnamed condition with one typed input and an execution context.</summary>
    public static ConditionNode WaitForCondition<T1>(
        this IFlowContext context,
        string id,
        TimeSpan pollingInterval,
        Node<T1> input1,
        Func<T1, IStepExecutionContext, ValueTask<bool>> condition)
        => WaitForCondition(context, id, id, pollingInterval, input1, condition);

    /// <summary>Declares a named condition with one typed input and an execution context.</summary>
    public static ConditionNode WaitForCondition<T1>(
        this IFlowContext context,
        string id,
        string name,
        TimeSpan pollingInterval,
        Node<T1> input1,
        Func<T1, IStepExecutionContext, ValueTask<bool>> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return context.WaitForCondition(
            id,
            name,
            pollingInterval,
            [input1],
            (inputs, executionContext) => condition(inputs.Get<T1>(0), executionContext));
    }

    /// <summary>Declares an unnamed condition with two typed node inputs.</summary>
    public static ConditionNode WaitForCondition<T1, T2>(
        this IFlowContext context,
        string id,
        TimeSpan pollingInterval,
        Node<T1> input1,
        Node<T2> input2,
        Func<T1, T2, ValueTask<bool>> condition)
        => WaitForCondition(context, id, id, pollingInterval, input1, input2, condition);

    /// <summary>Declares a named condition with two typed node inputs.</summary>
    public static ConditionNode WaitForCondition<T1, T2>(
        this IFlowContext context,
        string id,
        string name,
        TimeSpan pollingInterval,
        Node<T1> input1,
        Node<T2> input2,
        Func<T1, T2, ValueTask<bool>> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return context.WaitForCondition(
            id,
            name,
            pollingInterval,
            [input1, input2],
            (inputs, _) => condition(inputs.Get<T1>(0), inputs.Get<T2>(1)));
    }

    /// <summary>Declares an unnamed condition with two typed inputs and an execution context.</summary>
    public static ConditionNode WaitForCondition<T1, T2>(
        this IFlowContext context,
        string id,
        TimeSpan pollingInterval,
        Node<T1> input1,
        Node<T2> input2,
        Func<T1, T2, IStepExecutionContext, ValueTask<bool>> condition)
        => WaitForCondition(context, id, id, pollingInterval, input1, input2, condition);

    /// <summary>Declares a named condition with two typed inputs and an execution context.</summary>
    public static ConditionNode WaitForCondition<T1, T2>(
        this IFlowContext context,
        string id,
        string name,
        TimeSpan pollingInterval,
        Node<T1> input1,
        Node<T2> input2,
        Func<T1, T2, IStepExecutionContext, ValueTask<bool>> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return context.WaitForCondition(
            id,
            name,
            pollingInterval,
            [input1, input2],
            (inputs, executionContext) => condition(
                inputs.Get<T1>(0),
                inputs.Get<T2>(1),
                executionContext));
    }
}

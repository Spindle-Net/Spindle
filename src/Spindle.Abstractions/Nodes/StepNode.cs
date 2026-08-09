using Spindle.Abstractions.Steps;

namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Represents an executable step node.
/// </summary>
/// <typeparam name="TResult">The step result type.</typeparam>
public abstract class StepNode<TResult> : Node<TResult>
{
    public abstract StepOptions Options { get; }

    public abstract StepNode<TResult> WithOptions(
        Func<StepOptions, StepOptions> configure);
}

using System.Runtime.CompilerServices;

namespace Spindle.Abstractions.Steps;

public abstract class Step<T> : Step
{
    /// <summary>
    /// Gets the completed result of this step.
    ///
    /// If the step is not yet complete, the runtime should suspend flow expansion
    /// and resume/replay the flow when the step has completed.
    /// </summary>
    public abstract ValueTask<T> GetResultAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes Step&lt;T&gt; directly awaitable:
    ///
    /// var result = await step;
    /// </summary>
    public ValueTaskAwaiter<T> GetAwaiter()
    {
        return GetResultAsync().GetAwaiter();
    }

    /// <summary>
    /// Returns a typed copy of this step with updated options.
    /// </summary>
    public abstract override Step<T> WithOptions(
        Func<StepOptions, StepOptions> configure);

    /// <summary>
    /// Waits for the step without caring about the result.
    /// </summary>
    public sealed override async ValueTask WaitAsync(
        CancellationToken cancellationToken = default)
    {
        _ = await GetResultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
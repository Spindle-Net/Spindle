using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Steps;

public abstract class Step
{
    public abstract StepId Id { get; }

    public abstract string Name { get; }

    public abstract StepKind Kind { get; }

    public abstract StepOptions Options { get; }

    /// <summary>
    /// Returns a copy of this step with updated options.
    /// Implementations may use this to support fluent configuration
    /// such as .OnQueue(...), .WithRetry(...), etc.
    /// </summary>
    public abstract Step WithOptions(
        Func<StepOptions, StepOptions> configure);

    /// <summary>
    /// Waits until this step has completed without returning a result.
    /// </summary>
    public abstract ValueTask WaitAsync(
        CancellationToken cancellationToken = default);

    public override string ToString()
    {
        return $"{Name} ({Id})";
    }
}
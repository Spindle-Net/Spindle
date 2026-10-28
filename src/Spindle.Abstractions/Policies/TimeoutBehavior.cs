namespace Spindle.Abstractions.Policies;

public enum TimeoutBehavior
{
    /// <summary>
    /// Fail the attempt. The step is retried when its retry policy allows it, otherwise it ends as timed out.
    /// </summary>
    Fail,

    /// <summary>
    /// Mark the step as timed out and cancel the flow, without retrying.
    /// </summary>
    Cancel,

    /// <summary>
    /// Complete the step with the default value of its result type and continue the flow, without retrying.
    /// </summary>
    Continue
}

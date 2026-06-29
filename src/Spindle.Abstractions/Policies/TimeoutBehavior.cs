namespace Spindle.Abstractions.Policies;

public enum TimeoutBehavior
{
    /// <summary>
    /// Fail the current step if it times out
    /// </summary>
    Fail,

    /// <summary>
    /// Cancel the flow if the step times out
    /// </summary>
    Cancel,

    /// <summary>
    /// Continue with the rest of the flow if the timeout is hit
    /// </summary>
    Continue
}
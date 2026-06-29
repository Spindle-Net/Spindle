namespace Spindle.Abstractions.Steps;

public enum StepKind
{
    /// <summary>
    /// A regular step
    /// </summary>
    Step,

    /// <summary>
    /// A timer that is waiting until a certain time before starting the flow again
    /// </summary>
    Timer,

    /// <summary>
    /// Wait for a signal to trigger the continuation of the flow
    /// </summary>
    SignalWait,

    /// <summary>
    /// A remote flow
    /// </summary>
    RemoteFlow,

    /// <summary>
    /// A subflow in the same application
    /// </summary>
    SubFlow
}
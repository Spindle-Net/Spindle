namespace Spindle.Abstractions.Nodes;

public enum NodeKind
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
    SubFlow,

    /// <summary>
    /// A barrier that completes after all of its inputs satisfy its completion mode.
    /// </summary>
    WaitAll,

    /// <summary>
    /// A barrier that completes after one of its inputs satisfies its completion mode.
    /// </summary>
    WaitAny
}

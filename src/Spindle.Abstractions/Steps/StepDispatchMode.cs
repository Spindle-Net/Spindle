namespace Spindle.Abstractions.Steps;

public enum StepDispatchMode
{
    /// <summary>
    /// The step will be executed immediately in the current thread.
    /// </summary>
    Immediate,

    /// <summary>
    /// The step will be executed on a local worker without re-queueing the job.
    /// </summary>
    LocalWorker,

    /// <summary>
    /// The step will be dispatched to a queue for execution.
    /// </summary>
    Queued
}
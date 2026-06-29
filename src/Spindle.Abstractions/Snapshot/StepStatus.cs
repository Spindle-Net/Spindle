namespace Spindle.Abstractions.Snapshot;

public enum StepStatus
{
    Pending,
    Ready,
    Dispatching,
    Dispatched,
    Running,
    Waiting,
    Completed,
    Failed,
    Cancelled,
    TimedOut,
    Skipped
}
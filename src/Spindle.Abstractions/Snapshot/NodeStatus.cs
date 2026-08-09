namespace Spindle.Abstractions.Snapshot;

public enum NodeStatus
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
namespace Spindle.Abstractions.Snapshot;

public enum FlowInstanceStatus
{
    Pending,
    Running,
    Waiting,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}
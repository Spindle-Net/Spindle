using Spindle.Abstractions.Snapshot;

namespace Spindle;

internal readonly record struct StepExecutionResult(
    bool Executed,
    NodeStatus Status)
{
    public static StepExecutionResult NotExecuted { get; } = new(
        Executed: false,
        NodeStatus.Ready);

    public static StepExecutionResult Failed { get; } = new(
        Executed: true,
        NodeStatus.Failed);

    public static StepExecutionResult Succeeded { get; } = new(
        Executed: true,
        NodeStatus.Completed);

    public static StepExecutionResult Waiting { get; } = new(
        Executed: true,
        NodeStatus.Waiting);

    public static StepExecutionResult TimedOut { get; } = new(
        Executed: true,
        NodeStatus.TimedOut);
}

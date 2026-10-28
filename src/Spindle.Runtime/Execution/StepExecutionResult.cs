using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.Nodes;

namespace Spindle;

/// <param name="Executed">Whether the executor ran an attempt of the step.</param>
/// <param name="Status">The step status after execution.</param>
/// <param name="Node">The persisted step after execution, when the executor read it back.</param>
internal readonly record struct StepExecutionResult(
    bool Executed,
    NodeStatus Status,
    NodeInstanceRecord? Node = null)
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

    public static StepExecutionResult From(NodeInstanceRecord node) => new(
        Executed: true,
        node.Status,
        node);
}

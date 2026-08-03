using Spindle.Abstractions.Steps;
using Spindle.Persistence.Steps;

namespace Spindle;

internal interface IStepExecutor
{

    public bool SupportsDispatchMode(StepDispatchMode mode);

    public Task<StepExecutionResult> ExecuteAsync(
        FlowExecutionSession session,
        StepInstanceRecord record,
        CancellationToken cancellationToken);

}

internal readonly record struct StepExecutionResult(
    bool Executed,
    bool Completed)
{
    public static StepExecutionResult NotExecuted { get; } = new(
        Executed: false,
        Completed: false);

    public static StepExecutionResult Failed { get; } = new(
        Executed: true,
        Completed: false);

    public static StepExecutionResult Succeeded { get; } = new(
        Executed: true,
        Completed: true);
}

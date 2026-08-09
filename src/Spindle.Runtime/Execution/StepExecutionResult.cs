namespace Spindle;

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

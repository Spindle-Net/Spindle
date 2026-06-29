namespace Spindle.Abstractions.Steps;

public delegate ValueTask<TResult> StepCallback<TResult>(
    StepInputs inputs,
    IStepExecutionContext context);
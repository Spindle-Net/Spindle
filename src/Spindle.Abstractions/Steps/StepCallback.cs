using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle.Abstractions.Steps;

public delegate ValueTask<TResult> StepCallback<TResult>(
    NodeInputs inputs,
    IStepExecutionContext context);

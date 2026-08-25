using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle.Abstractions.Waiting;

/// <summary>
/// Evaluates whether a durable condition wait has completed.
/// </summary>
public delegate ValueTask<bool> ConditionCallback(
    NodeInputs inputs,
    IStepExecutionContext context);

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle;

internal sealed record StepExecutionRegistration(
    NodeId NodeId,
    Type ResultType,
    IReadOnlyList<Type> DependencyResultTypes,
    Func<NodeInputs, IStepExecutionContext, ValueTask<object?>> Execute);

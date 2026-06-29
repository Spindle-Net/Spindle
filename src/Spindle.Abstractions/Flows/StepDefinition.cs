using Spindle.Abstractions.Core;
using Spindle.Abstractions.Steps;

namespace Spindle.Abstractions.Flows;

public sealed record StepDefinition
{
    public required StepId Id { get; init; }

    public required string Name { get; init; }

    public required StepKind Kind { get; init; }

    public StepHandlerId? HandlerId { get; init; }

    public QueueName? Queue { get; init; }

    public Type? InputType { get; init; }

    public Type? ResultType { get; init; }

    public StepOptions? Options { get; init; }

    public IReadOnlyList<StepId> Dependencies { get; init; }
        = [];
}
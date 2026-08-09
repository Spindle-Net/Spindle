using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle.Abstractions.Flows;

public sealed record NodeDefinition
{
    public required NodeId Id { get; init; }

    public required string Name { get; init; }

    public required NodeKind Kind { get; init; }

    public StepHandlerId? HandlerId { get; init; }

    public QueueName? Queue { get; init; }

    public Type? InputType { get; init; }

    public Type? ResultType { get; init; }

    public StepOptions? Options { get; init; }

    public DependencySatisfactionMode DependencyMode { get; init; }
        = DependencySatisfactionMode.AllSucceeded;

    public IReadOnlyList<NodeId> Dependencies { get; init; }
        = [];
}

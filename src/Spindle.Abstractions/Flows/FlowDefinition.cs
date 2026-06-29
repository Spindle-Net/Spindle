using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Flows;

public sealed record FlowDefinition
{
    public required FlowName Name { get; init; }

    public required FlowVersion Version { get; init; }

    public required string DefinitionHash { get; init; }

    public IReadOnlyList<StepDefinition> Steps { get; init; }
        = [];

    public IReadOnlyList<StepEdge> Edges { get; init; }
        = [];
}
using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Flows;

public sealed record FlowDefinition
{
    public required FlowName Name { get; init; }

    public required FlowVersion Version { get; init; }

    public required string DefinitionHash { get; init; }

    public IReadOnlyList<NodeDefinition> Nodes { get; init; }
        = [];

    public IReadOnlyList<NodeEdge> Edges { get; init; }
        = [];
}

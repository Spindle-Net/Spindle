using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Flows;

public sealed record NodeEdge
{
    public required NodeId From { get; init; }

    public required NodeId To { get; init; }

    public string? Label { get; init; }
}
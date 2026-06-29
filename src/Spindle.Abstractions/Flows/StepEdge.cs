using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Flows;

public sealed record StepEdge
{
    public required StepId From { get; init; }

    public required StepId To { get; init; }

    public string? Label { get; init; }
}
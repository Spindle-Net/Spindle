using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Flows;

public sealed record StartFlowOptions
{
    public FlowVersion? Version { get; init; }

    public CorrelationKey? CorrelationKey { get; init; }

    public string? IdempotencyKey { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>();
}
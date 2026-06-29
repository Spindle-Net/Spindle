using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.FlowDefinitions;

public sealed record FlowDefinitionRecord
{
    public required FlowName FlowName { get; init; }

    public required FlowVersion FlowVersion { get; init; }

    public required string DefinitionHash { get; init; }

    public required string FlowTypeName { get; init; }

    public SerializedPayload? Definition { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

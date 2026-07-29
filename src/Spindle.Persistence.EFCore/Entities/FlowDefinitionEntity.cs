using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowName), nameof(FlowVersion))]
internal class FlowDefinitionEntity
{

    public required string FlowName { get; init; }

    public required string FlowVersion { get; init; }

    public required string DefinitionHash { get; set; }

    public required string FlowTypeName { get; set; }

    public SerializedPayload? Definition { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; set; }

    public ICollection<FlowInstanceEntity>? Instances { get; set; }

}

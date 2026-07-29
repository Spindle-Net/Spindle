using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Snapshot;
using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(InstanceId))]
[Index(nameof(FlowName), nameof(IdempotencyKey), IsUnique = true)]
[Index(nameof(Status), nameof(UpdatedAt))]
internal class FlowInstanceEntity
{

    [MaxLength(255)]
    public required string InstanceId { get; init; }

    [MaxLength(255)]
    public required string FlowName { get; init; }

    [MaxLength(255)]
    public required string FlowVersion { get; init; }

    public required string DefinitionHash { get; init; }

    public required FlowInstanceStatus Status { get; set; }

    public required SerializedPayload Input { get; init; }

    public SerializedPayload? Result { get; set; }

    public string? Error { get; set; }

    public string? CorrelationKey { get; init; }

    [MaxLength(255)]
    public string? IdempotencyKey { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

}

using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(NodeId))]
[Index(nameof(FlowInstanceId), nameof(Status))]
[Index(nameof(Status), nameof(CreatedAt))]
internal class NodeInstanceEntity
{
    [MaxLength(255)]
    public required string FlowInstanceId { get; init; }

    [MaxLength(255)]
    public required string NodeId { get; init; }

    public required string Name { get; init; }

    public required NodeKind Kind { get; init; }

    public required NodeStatus Status { get; set; }

    public string? HandlerId { get; init; }

    public string? Queue { get; init; }

    public StepDispatchMode DispatchMode { get; init; }

    public DependencySatisfactionMode DependencyMode { get; init; }

    /// <summary>
    /// Nodes that this node depends on.
    /// </summary>
    public List<NodeDependencyEntity> Dependencies { get; init; } = [];

    /// <summary>
    /// Nodes that depend on this node.
    /// </summary>
    public List<NodeDependencyEntity> Dependents { get; init; } = [];

    public SerializedPayload? Input { get; init; }

    public SerializedPayload? Result { get; set; }

    public string? Error { get; set; }

    public int Attempt { get; set; }

    public DateTimeOffset? RetryAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

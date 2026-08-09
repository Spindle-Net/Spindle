using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
namespace Spindle.Persistence.EFCore.Entities;

[Table("NodeDependencies")]
[PrimaryKey(nameof(FlowInstanceId), nameof(NodeId), nameof(DependsOnId))]
[Index(nameof(FlowInstanceId), nameof(NodeId))]
[Index(nameof(FlowInstanceId), nameof(DependsOnId))]
internal class NodeDependencyEntity
{

    public required string FlowInstanceId { get; set; }
    public required string NodeId { get; set; }

    public NodeInstanceEntity? Node { get; set; }

    public required string DependsOnId { get; set; }
    public NodeInstanceEntity? DependsOn { get; set; }

    public int Position { get; set; }

}

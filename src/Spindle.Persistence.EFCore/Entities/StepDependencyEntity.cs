using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
namespace Spindle.Persistence.EFCore.Entities;

[Table("StepDependencies")]
[PrimaryKey(nameof(FlowInstanceId), nameof(StepId), nameof(DependsOnId))]
internal class StepDependencyEntity
{

    public required string FlowInstanceId { get; set; }
    public required string StepId { get; set; }

    public StepInstanceEntity? Step { get; set; }

    public required string DependsOnId { get; set; }
    public StepInstanceEntity? DependsOn { get; set; }

}

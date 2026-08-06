using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spindle.Persistence.EFCore.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Use_A_Better_Index_For_MarkDependentsReadyAsync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StepInstances_FlowInstanceId_StepId_Status",
                table: "StepInstances");

            migrationBuilder.CreateIndex(
                name: "IX_StepInstances_FlowInstanceId_Status",
                table: "StepInstances",
                columns: new[] { "FlowInstanceId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StepInstances_FlowInstanceId_Status",
                table: "StepInstances");

            migrationBuilder.CreateIndex(
                name: "IX_StepInstances_FlowInstanceId_StepId_Status",
                table: "StepInstances",
                columns: new[] { "FlowInstanceId", "StepId", "Status" });
        }
    }
}

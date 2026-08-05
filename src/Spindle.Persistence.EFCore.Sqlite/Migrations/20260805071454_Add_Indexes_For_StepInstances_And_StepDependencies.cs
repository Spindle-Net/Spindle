using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spindle.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Add_Indexes_For_StepInstances_And_StepDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StepInstances_FlowInstanceId_StepId_Status",
                table: "StepInstances",
                columns: new[] { "FlowInstanceId", "StepId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StepDependencies_FlowInstanceId_StepId",
                table: "StepDependencies",
                columns: new[] { "FlowInstanceId", "StepId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StepInstances_FlowInstanceId_StepId_Status",
                table: "StepInstances");

            migrationBuilder.DropIndex(
                name: "IX_StepDependencies_FlowInstanceId_StepId",
                table: "StepDependencies");
        }
    }
}

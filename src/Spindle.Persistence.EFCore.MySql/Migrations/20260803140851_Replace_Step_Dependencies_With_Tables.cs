using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spindle.Persistence.EFCore.MySql.Migrations
{
    /// <inheritdoc />
    public partial class Replace_Step_Dependencies_With_Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Dependencies",
                table: "StepInstances");

            migrationBuilder.CreateTable(
                name: "StepDependencies",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "varchar(255)", nullable: false),
                    StepId = table.Column<string>(type: "varchar(255)", nullable: false),
                    DependsOnId = table.Column<string>(type: "varchar(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepDependencies", x => new { x.FlowInstanceId, x.StepId, x.DependsOnId });
                    table.ForeignKey(
                        name: "FK_StepDependencies_StepInstances_FlowInstanceId_DependsOnId",
                        columns: x => new { x.FlowInstanceId, x.DependsOnId },
                        principalTable: "StepInstances",
                        principalColumns: new[] { "FlowInstanceId", "StepId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StepDependencies_StepInstances_FlowInstanceId_StepId",
                        columns: x => new { x.FlowInstanceId, x.StepId },
                        principalTable: "StepInstances",
                        principalColumns: new[] { "FlowInstanceId", "StepId" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StepDependencies_FlowInstanceId_DependsOnId",
                table: "StepDependencies",
                columns: new[] { "FlowInstanceId", "DependsOnId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StepDependencies");

            migrationBuilder.AddColumn<string>(
                name: "Dependencies",
                table: "StepInstances",
                type: "json",
                nullable: false);
        }
    }
}

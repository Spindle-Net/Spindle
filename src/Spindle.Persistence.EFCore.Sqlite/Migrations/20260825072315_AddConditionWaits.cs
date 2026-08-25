using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spindle.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddConditionWaits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConditionWaits",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PollingIntervalTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionWaits", x => new { x.FlowInstanceId, x.NodeId });
                    table.ForeignKey(
                        name: "FK_ConditionWaits_NodeInstances_FlowInstanceId_NodeId",
                        columns: x => new { x.FlowInstanceId, x.NodeId },
                        principalTable: "NodeInstances",
                        principalColumns: new[] { "FlowInstanceId", "NodeId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConditionWaits_ExpiresAt",
                table: "ConditionWaits",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConditionWaits");
        }
    }
}

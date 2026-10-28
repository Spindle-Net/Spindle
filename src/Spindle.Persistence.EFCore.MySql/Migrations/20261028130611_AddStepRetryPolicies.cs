using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spindle.Persistence.EFCore.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddStepRetryPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryAttemptBaseline",
                table: "NodeInstances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RetryJson",
                table: "NodeInstances",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeoutJson",
                table: "NodeInstances",
                type: "longtext",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryAttemptBaseline",
                table: "NodeInstances");

            migrationBuilder.DropColumn(
                name: "RetryJson",
                table: "NodeInstances");

            migrationBuilder.DropColumn(
                name: "TimeoutJson",
                table: "NodeInstances");
        }
    }
}

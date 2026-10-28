using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spindle.Persistence.EFCore.Sqlite.Migrations
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
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RetryPolicy",
                table: "NodeInstances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeoutPolicy",
                table: "NodeInstances",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryAttemptBaseline",
                table: "NodeInstances");

            migrationBuilder.DropColumn(
                name: "RetryPolicy",
                table: "NodeInstances");

            migrationBuilder.DropColumn(
                name: "TimeoutPolicy",
                table: "NodeInstances");
        }
    }
}

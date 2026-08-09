using Microsoft.EntityFrameworkCore.Migrations;
using Spindle.Persistence.EFCore.Migrations;

#nullable disable

namespace Spindle.Persistence.EFCore.SqlServer.Migrations;

/// <inheritdoc />
public partial class GenericNodes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
        => GenericNodesMigration.Up(migrationBuilder);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => GenericNodesMigration.Down(migrationBuilder);
}

using Microsoft.EntityFrameworkCore.Migrations;

namespace Spindle.Persistence.EFCore.Migrations;

public static class GenericNodesMigration
{
    public static void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_StepDependencies_StepInstances_FlowInstanceId_DependsOnId",
            table: "StepDependencies");
        migrationBuilder.DropForeignKey(
            name: "FK_StepDependencies_StepInstances_FlowInstanceId_StepId",
            table: "StepDependencies");
        migrationBuilder.DropPrimaryKey(name: "PK_StepDependencies", table: "StepDependencies");
        migrationBuilder.DropPrimaryKey(name: "PK_StepInstances", table: "StepInstances");

        migrationBuilder.RenameTable(name: "StepInstances", newName: "NodeInstances");
        migrationBuilder.RenameTable(name: "StepDependencies", newName: "NodeDependencies");
        migrationBuilder.RenameColumn(name: "StepId", table: "NodeInstances", newName: "NodeId");
        migrationBuilder.RenameColumn(name: "StepId", table: "NodeDependencies", newName: "NodeId");

        RenameNodeIdColumns(migrationBuilder, "StepId", "NodeId");

        migrationBuilder.RenameIndex(
            name: "IX_StepInstances_FlowInstanceId_Status",
            table: "NodeInstances",
            newName: "IX_NodeInstances_FlowInstanceId_Status");
        migrationBuilder.RenameIndex(
            name: "IX_StepInstances_Status_CreatedAt",
            table: "NodeInstances",
            newName: "IX_NodeInstances_Status_CreatedAt");
        migrationBuilder.RenameIndex(
            name: "IX_StepDependencies_FlowInstanceId_DependsOnId",
            table: "NodeDependencies",
            newName: "IX_NodeDependencies_FlowInstanceId_DependsOnId");
        migrationBuilder.RenameIndex(
            name: "IX_StepDependencies_FlowInstanceId_StepId",
            table: "NodeDependencies",
            newName: "IX_NodeDependencies_FlowInstanceId_NodeId");

        migrationBuilder.AddColumn<int>(
            name: "DependencyMode",
            table: "NodeInstances",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.AddColumn<int>(
            name: "Position",
            table: "NodeDependencies",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddPrimaryKey(
            name: "PK_NodeInstances",
            table: "NodeInstances",
            columns: ["FlowInstanceId", "NodeId"]);
        migrationBuilder.AddPrimaryKey(
            name: "PK_NodeDependencies",
            table: "NodeDependencies",
            columns: ["FlowInstanceId", "NodeId", "DependsOnId"]);
        migrationBuilder.AddForeignKey(
            name: "FK_NodeDependencies_NodeInstances_FlowInstanceId_DependsOnId",
            table: "NodeDependencies",
            columns: ["FlowInstanceId", "DependsOnId"],
            principalTable: "NodeInstances",
            principalColumns: ["FlowInstanceId", "NodeId"],
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_NodeDependencies_NodeInstances_FlowInstanceId_NodeId",
            table: "NodeDependencies",
            columns: ["FlowInstanceId", "NodeId"],
            principalTable: "NodeInstances",
            principalColumns: ["FlowInstanceId", "NodeId"],
            onDelete: ReferentialAction.Restrict);
    }

    public static void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_NodeDependencies_NodeInstances_FlowInstanceId_DependsOnId",
            table: "NodeDependencies");
        migrationBuilder.DropForeignKey(
            name: "FK_NodeDependencies_NodeInstances_FlowInstanceId_NodeId",
            table: "NodeDependencies");
        migrationBuilder.DropPrimaryKey(name: "PK_NodeDependencies", table: "NodeDependencies");
        migrationBuilder.DropPrimaryKey(name: "PK_NodeInstances", table: "NodeInstances");
        migrationBuilder.DropColumn(name: "Position", table: "NodeDependencies");
        migrationBuilder.DropColumn(name: "DependencyMode", table: "NodeInstances");

        migrationBuilder.RenameIndex(
            name: "IX_NodeInstances_FlowInstanceId_Status",
            table: "NodeInstances",
            newName: "IX_StepInstances_FlowInstanceId_Status");
        migrationBuilder.RenameIndex(
            name: "IX_NodeInstances_Status_CreatedAt",
            table: "NodeInstances",
            newName: "IX_StepInstances_Status_CreatedAt");
        migrationBuilder.RenameIndex(
            name: "IX_NodeDependencies_FlowInstanceId_DependsOnId",
            table: "NodeDependencies",
            newName: "IX_StepDependencies_FlowInstanceId_DependsOnId");
        migrationBuilder.RenameIndex(
            name: "IX_NodeDependencies_FlowInstanceId_NodeId",
            table: "NodeDependencies",
            newName: "IX_StepDependencies_FlowInstanceId_StepId");

        migrationBuilder.RenameColumn(name: "NodeId", table: "NodeInstances", newName: "StepId");
        migrationBuilder.RenameColumn(name: "NodeId", table: "NodeDependencies", newName: "StepId");
        RenameNodeIdColumns(migrationBuilder, "NodeId", "StepId");
        migrationBuilder.RenameTable(name: "NodeDependencies", newName: "StepDependencies");
        migrationBuilder.RenameTable(name: "NodeInstances", newName: "StepInstances");

        migrationBuilder.AddPrimaryKey(
            name: "PK_StepInstances",
            table: "StepInstances",
            columns: ["FlowInstanceId", "StepId"]);
        migrationBuilder.AddPrimaryKey(
            name: "PK_StepDependencies",
            table: "StepDependencies",
            columns: ["FlowInstanceId", "StepId", "DependsOnId"]);
        migrationBuilder.AddForeignKey(
            name: "FK_StepDependencies_StepInstances_FlowInstanceId_DependsOnId",
            table: "StepDependencies",
            columns: ["FlowInstanceId", "DependsOnId"],
            principalTable: "StepInstances",
            principalColumns: ["FlowInstanceId", "StepId"],
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_StepDependencies_StepInstances_FlowInstanceId_StepId",
            table: "StepDependencies",
            columns: ["FlowInstanceId", "StepId"],
            principalTable: "StepInstances",
            principalColumns: ["FlowInstanceId", "StepId"],
            onDelete: ReferentialAction.Restrict);
    }

    private static void RenameNodeIdColumns(
        MigrationBuilder migrationBuilder,
        string oldName,
        string newName)
    {
        foreach (var table in new[]
        {
            "Timers",
            "StepLeases",
            "StepAttempts",
            "SignalWaits",
            "ExecutionHistories"
        })
        {
            migrationBuilder.RenameColumn(name: oldName, table: table, newName: newName);
        }
    }
}

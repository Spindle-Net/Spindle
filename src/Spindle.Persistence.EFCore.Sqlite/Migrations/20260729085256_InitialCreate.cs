using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spindle.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FlowInstanceId = table.Column<string>(type: "TEXT", nullable: false),
                    StepId = table.Column<string>(type: "TEXT", nullable: true),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlowDefinitions",
                columns: table => new
                {
                    FlowName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FlowVersion = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DefinitionHash = table.Column<string>(type: "TEXT", nullable: false),
                    FlowTypeName = table.Column<string>(type: "TEXT", nullable: false),
                    Definition = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowDefinitions", x => new { x.FlowName, x.FlowVersion });
                });

            migrationBuilder.CreateTable(
                name: "FlowInstances",
                columns: table => new
                {
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FlowName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FlowVersion = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DefinitionHash = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Input = table.Column<string>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CorrelationKey = table.Column<string>(type: "TEXT", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowInstances", x => x.InstanceId);
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    ReceivedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    Headers = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "Signals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SignalName = table.Column<string>(type: "TEXT", nullable: false),
                    CorrelationKey = table.Column<string>(type: "TEXT", nullable: true),
                    FlowInstanceId = table.Column<string>(type: "TEXT", nullable: true),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    RaisedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignalWaits",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    SignalName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CorrelationKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalWaits", x => new { x.FlowInstanceId, x.StepId });
                });

            migrationBuilder.CreateTable(
                name: "StepAttempts",
                columns: table => new
                {
                    AttemptId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FlowInstanceId = table.Column<string>(type: "TEXT", nullable: false),
                    StepId = table.Column<string>(type: "TEXT", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkerId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepAttempts", x => x.AttemptId);
                });

            migrationBuilder.CreateTable(
                name: "StepInstances",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    HandlerId = table.Column<string>(type: "TEXT", nullable: true),
                    Queue = table.Column<string>(type: "TEXT", nullable: true),
                    DispatchMode = table.Column<int>(type: "INTEGER", nullable: false),
                    Dependencies = table.Column<string>(type: "TEXT", nullable: false),
                    Input = table.Column<string>(type: "TEXT", nullable: true),
                    Result = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryAt = table.Column<long>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepInstances", x => new { x.FlowInstanceId, x.StepId });
                });

            migrationBuilder.CreateTable(
                name: "StepLeases",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    AcquiredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepLeases", x => new { x.FlowInstanceId, x.StepId });
                });

            migrationBuilder.CreateTable(
                name: "Timers",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DueAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    FiredAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timers", x => new { x.FlowInstanceId, x.StepId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlowInstances_FlowName_IdempotencyKey",
                table: "FlowInstances",
                columns: new[] { "FlowName", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlowInstances_Status_UpdatedAt",
                table: "FlowInstances",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAt_CreatedAt",
                table: "OutboxMessages",
                columns: new[] { "PublishedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SignalWaits_SignalName_CorrelationKey_CompletedAt",
                table: "SignalWaits",
                columns: new[] { "SignalName", "CorrelationKey", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StepInstances_Status_CreatedAt",
                table: "StepInstances",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Timers_FiredAt_DueAt",
                table: "Timers",
                columns: new[] { "FiredAt", "DueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionHistories");

            migrationBuilder.DropTable(
                name: "FlowDefinitions");

            migrationBuilder.DropTable(
                name: "FlowInstances");

            migrationBuilder.DropTable(
                name: "InboxMessages");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "Signals");

            migrationBuilder.DropTable(
                name: "SignalWaits");

            migrationBuilder.DropTable(
                name: "StepAttempts");

            migrationBuilder.DropTable(
                name: "StepInstances");

            migrationBuilder.DropTable(
                name: "StepLeases");

            migrationBuilder.DropTable(
                name: "Timers");
        }
    }
}

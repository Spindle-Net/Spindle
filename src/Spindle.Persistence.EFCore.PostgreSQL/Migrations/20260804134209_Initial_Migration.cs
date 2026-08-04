using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Spindle.Persistence.EFCore.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Initial_Migration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FlowInstanceId = table.Column<string>(type: "text", nullable: false),
                    StepId = table.Column<string>(type: "text", nullable: true),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Payload_ContentType = table.Column<string>(type: "text", nullable: true),
                    Payload_TypeName = table.Column<string>(type: "text", nullable: true),
                    Payload_Data = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlowDefinitions",
                columns: table => new
                {
                    FlowName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FlowVersion = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DefinitionHash = table.Column<string>(type: "text", nullable: false),
                    FlowTypeName = table.Column<string>(type: "text", nullable: false),
                    Definition_ContentType = table.Column<string>(type: "text", nullable: true),
                    Definition_TypeName = table.Column<string>(type: "text", nullable: true),
                    Definition_Data = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowDefinitions", x => new { x.FlowName, x.FlowVersion });
                });

            migrationBuilder.CreateTable(
                name: "FlowInstances",
                columns: table => new
                {
                    InstanceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FlowName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FlowVersion = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DefinitionHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Result_ContentType = table.Column<string>(type: "text", nullable: true),
                    Result_TypeName = table.Column<string>(type: "text", nullable: true),
                    Result_Data = table.Column<byte[]>(type: "bytea", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CorrelationKey = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Input_ContentType = table.Column<string>(type: "text", nullable: false),
                    Input_Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    Input_TypeName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowInstances", x => x.InstanceId);
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Payload_ContentType = table.Column<string>(type: "text", nullable: false),
                    Payload_Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    Payload_TypeName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Headers = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Payload_ContentType = table.Column<string>(type: "text", nullable: false),
                    Payload_Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    Payload_TypeName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "Signals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SignalName = table.Column<string>(type: "text", nullable: false),
                    CorrelationKey = table.Column<string>(type: "text", nullable: true),
                    FlowInstanceId = table.Column<string>(type: "text", nullable: true),
                    RaisedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Payload_ContentType = table.Column<string>(type: "text", nullable: false),
                    Payload_Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    Payload_TypeName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignalWaits",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StepId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SignalName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CorrelationKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalWaits", x => new { x.FlowInstanceId, x.StepId });
                });

            migrationBuilder.CreateTable(
                name: "StepAttempts",
                columns: table => new
                {
                    AttemptId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FlowInstanceId = table.Column<string>(type: "text", nullable: false),
                    StepId = table.Column<string>(type: "text", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    WorkerId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepAttempts", x => x.AttemptId);
                });

            migrationBuilder.CreateTable(
                name: "StepInstances",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StepId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    HandlerId = table.Column<string>(type: "text", nullable: true),
                    Queue = table.Column<string>(type: "text", nullable: true),
                    DispatchMode = table.Column<int>(type: "integer", nullable: false),
                    Input_ContentType = table.Column<string>(type: "text", nullable: true),
                    Input_TypeName = table.Column<string>(type: "text", nullable: true),
                    Input_Data = table.Column<byte[]>(type: "bytea", nullable: true),
                    Result_ContentType = table.Column<string>(type: "text", nullable: true),
                    Result_TypeName = table.Column<string>(type: "text", nullable: true),
                    Result_Data = table.Column<byte[]>(type: "bytea", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    RetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepInstances", x => new { x.FlowInstanceId, x.StepId });
                });

            migrationBuilder.CreateTable(
                name: "StepLeases",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StepId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Owner = table.Column<string>(type: "text", nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepLeases", x => new { x.FlowInstanceId, x.StepId });
                });

            migrationBuilder.CreateTable(
                name: "Timers",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StepId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timers", x => new { x.FlowInstanceId, x.StepId });
                });

            migrationBuilder.CreateTable(
                name: "StepDependencies",
                columns: table => new
                {
                    FlowInstanceId = table.Column<string>(type: "character varying(255)", nullable: false),
                    StepId = table.Column<string>(type: "character varying(255)", nullable: false),
                    DependsOnId = table.Column<string>(type: "character varying(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepDependencies", x => new { x.FlowInstanceId, x.StepId, x.DependsOnId });
                    table.ForeignKey(
                        name: "FK_StepDependencies_StepInstances_FlowInstanceId_DependsOnId",
                        columns: x => new { x.FlowInstanceId, x.DependsOnId },
                        principalTable: "StepInstances",
                        principalColumns: new[] { "FlowInstanceId", "StepId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StepDependencies_StepInstances_FlowInstanceId_StepId",
                        columns: x => new { x.FlowInstanceId, x.StepId },
                        principalTable: "StepInstances",
                        principalColumns: new[] { "FlowInstanceId", "StepId" },
                        onDelete: ReferentialAction.Restrict);
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
                name: "IX_StepDependencies_FlowInstanceId_DependsOnId",
                table: "StepDependencies",
                columns: new[] { "FlowInstanceId", "DependsOnId" });

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
                name: "StepDependencies");

            migrationBuilder.DropTable(
                name: "StepLeases");

            migrationBuilder.DropTable(
                name: "Timers");

            migrationBuilder.DropTable(
                name: "StepInstances");
        }
    }
}

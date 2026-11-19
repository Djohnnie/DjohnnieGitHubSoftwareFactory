using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddAgentRunModelUsages : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "UsageCacheReadTokens",
            table: "AiModels",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "UsageCacheWriteTokens",
            table: "AiModels",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "UsageInputTokens",
            table: "AiModels",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "UsageNanoAiu",
            table: "AiModels",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "UsageOutputTokens",
            table: "AiModels",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "UsageReasoningTokens",
            table: "AiModels",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<int>(
            name: "UsageRequests",
            table: "AiModels",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "UsageSessions",
            table: "AiModels",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "AgentRunModelUsages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AgentRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AiModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Attempt = table.Column<int>(type: "int", nullable: false),
                Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                NanoAiu = table.Column<long>(type: "bigint", nullable: false),
                Complete = table.Column<bool>(type: "bit", nullable: false),
                Requests = table.Column<int>(type: "int", nullable: false),
                PremiumRequests = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                InputTokens = table.Column<long>(type: "bigint", nullable: false),
                OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                CacheReadTokens = table.Column<long>(type: "bigint", nullable: false),
                CacheWriteTokens = table.Column<long>(type: "bigint", nullable: false),
                ReasoningTokens = table.Column<long>(type: "bigint", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                SysId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AgentRunModelUsages", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
                table.UniqueConstraint("AK_AgentRunModelUsages_SysId", x => x.SysId)
                    .Annotation("SqlServer:Clustered", true);
                table.ForeignKey(
                    name: "FK_AgentRunModelUsages_AgentRuns_AgentRunId",
                    column: x => x.AgentRunId,
                    principalTable: "AgentRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AgentRunModelUsages_AiModels_AiModelId",
                    column: x => x.AiModelId,
                    principalTable: "AiModels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AgentRunModelUsages_AgentRunId",
            table: "AgentRunModelUsages",
            column: "AgentRunId");

        migrationBuilder.CreateIndex(
            name: "IX_AgentRunModelUsages_AiModelId",
            table: "AgentRunModelUsages",
            column: "AiModelId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AgentRunModelUsages");

        migrationBuilder.DropColumn(
            name: "UsageCacheReadTokens",
            table: "AiModels");

        migrationBuilder.DropColumn(
            name: "UsageCacheWriteTokens",
            table: "AiModels");

        migrationBuilder.DropColumn(
            name: "UsageInputTokens",
            table: "AiModels");

        migrationBuilder.DropColumn(
            name: "UsageNanoAiu",
            table: "AiModels");

        migrationBuilder.DropColumn(
            name: "UsageOutputTokens",
            table: "AiModels");

        migrationBuilder.DropColumn(
            name: "UsageReasoningTokens",
            table: "AiModels");

        migrationBuilder.DropColumn(
            name: "UsageRequests",
            table: "AiModels");

        migrationBuilder.DropColumn(
            name: "UsageSessions",
            table: "AiModels");
    }
}

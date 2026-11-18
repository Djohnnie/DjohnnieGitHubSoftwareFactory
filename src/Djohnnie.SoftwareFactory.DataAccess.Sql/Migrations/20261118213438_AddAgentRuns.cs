using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddAgentRuns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Executor",
            table: "WorkItems",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "AgentRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RepositoryFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Attempts = table.Column<int>(type: "int", nullable: false),
                MaxAttempts = table.Column<int>(type: "int", nullable: false),
                RequestedModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Models = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Branch = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                PullRequestNumber = table.Column<int>(type: "int", nullable: true),
                PullRequestUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Summary = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                Error = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                NanoAiu = table.Column<long>(type: "bigint", nullable: true),
                UsageComplete = table.Column<bool>(type: "bit", nullable: false),
                PremiumRequests = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                InputTokens = table.Column<long>(type: "bigint", nullable: false),
                OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                CacheReadTokens = table.Column<long>(type: "bigint", nullable: false),
                CacheWriteTokens = table.Column<long>(type: "bigint", nullable: false),
                LeaseUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                QueuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                SysId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AgentRuns", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
                table.UniqueConstraint("AK_AgentRuns_SysId", x => x.SysId)
                    .Annotation("SqlServer:Clustered", true);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AgentRuns_Status",
            table: "AgentRuns",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_AgentRuns_WorkItemId",
            table: "AgentRuns",
            column: "WorkItemId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AgentRuns");

        migrationBuilder.DropColumn(
            name: "Executor",
            table: "WorkItems");
    }
}

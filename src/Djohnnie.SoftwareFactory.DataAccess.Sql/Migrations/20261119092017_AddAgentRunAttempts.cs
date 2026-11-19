using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddAgentRunAttempts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AgentRunAttempts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AgentRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Attempt = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
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
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                SysId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AgentRunAttempts", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
                table.UniqueConstraint("AK_AgentRunAttempts_SysId", x => x.SysId)
                    .Annotation("SqlServer:Clustered", true);
                table.ForeignKey(
                    name: "FK_AgentRunAttempts_AgentRuns_AgentRunId",
                    column: x => x.AgentRunId,
                    principalTable: "AgentRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AgentRunAttempts_AgentRunId_Attempt",
            table: "AgentRunAttempts",
            columns: new[] { "AgentRunId", "Attempt" },
            unique: true);

        // Tasks from before runs were stored only kept their last run: it becomes their only run. A task that waits for a
        // retry (pending or queued with attempts made) had that run fail.
        migrationBuilder.Sql("""
            INSERT INTO [AgentRunAttempts] ([Id], [AgentRunId], [Attempt], [Status], [Models], [Branch], [PullRequestNumber], [PullRequestUrl],
                [Summary], [Error], [NanoAiu], [UsageComplete], [PremiumRequests], [InputTokens], [OutputTokens], [CacheReadTokens],
                [CacheWriteTokens], [StartedAt], [CompletedAt])
            SELECT NEWID(), [Id], [Attempts],
                CASE WHEN [Status] IN ('Pending', 'Queued') THEN 'Failed' ELSE [Status] END,
                [Models], [Branch], [PullRequestNumber], [PullRequestUrl], [Summary], [Error], [NanoAiu], [UsageComplete], [PremiumRequests],
                [InputTokens], [OutputTokens], [CacheReadTokens], [CacheWriteTokens], COALESCE([StartedAt], [CreatedAt]),
                CASE WHEN [Status] = 'Running' THEN NULL ELSE COALESCE([CompletedAt], [StartedAt], [CreatedAt]) END
            FROM [AgentRuns]
            WHERE [Attempts] > 0
            ORDER BY [SysId];
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AgentRunAttempts");
    }
}

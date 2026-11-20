using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <summary>
/// At most one active (pending, queued or running) task of the DGHSF agent worker per work item, so two starts that race
/// can't both add or reopen one. Starts used to check that in a separate query, so a race may have left an item with two
/// active tasks: all but the newest are cancelled first, or the index couldn't be created.
/// </summary>
public partial class AddAgentRunActiveTaskIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            WITH Active AS (
                SELECT r.Id,
                    ROW_NUMBER() OVER (PARTITION BY r.RepositoryFullName, r.WorkItemId ORDER BY r.SysId DESC) AS Newest
                FROM AgentRuns r
                WHERE r.Status IN ('Pending', 'Queued', 'Running'))
            UPDATE r SET
                r.Status = 'Cancelled',
                r.Error = N'Cancelled because a newer task of the same work item was started at the same time.',
                r.LeaseUntil = NULL,
                r.CompletedAt = SYSDATETIMEOFFSET()
            FROM AgentRuns r
            JOIN Active a ON a.Id = r.Id
            WHERE a.Newest > 1;

            UPDATE a SET
                a.Status = 'Cancelled',
                a.CompletedAt = SYSDATETIMEOFFSET()
            FROM AgentRunAttempts a
            JOIN AgentRuns r ON r.Id = a.AgentRunId
            WHERE a.Status = 'Running' AND r.Status = 'Cancelled';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_AgentRuns_RepositoryFullName_WorkItemId_Active",
            table: "AgentRuns",
            columns: new[] { "RepositoryFullName", "WorkItemId" },
            unique: true,
            filter: "[Status] IN ('Pending', 'Queued', 'Running')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AgentRuns_RepositoryFullName_WorkItemId_Active",
            table: "AgentRuns");
    }
}

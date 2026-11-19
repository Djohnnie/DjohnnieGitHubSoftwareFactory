using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <summary>
/// A work item has one task of the DGHSF agent worker; every run on it (a retry, a fresh start or a revision on the pull
/// request's feedback) is a run of that task and says what it did. Folds the tasks that every follow-up run used to add into
/// the item's first task: their runs and model usage move to it (numbered after its own runs), their costs add up, and it
/// takes the outcome of the newest one. Items with a task that hasn't ended are left as they are.
/// </summary>
public partial class MergeAgentRunTasksPerWorkItem : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Kind",
            table: "AgentRunAttempts",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Implement");

        // Every run did what its task did.
        migrationBuilder.Sql("""
            UPDATE a SET a.Kind = r.Kind
            FROM AgentRunAttempts a
            JOIN AgentRuns r ON r.Id = a.AgentRunId;
            """);

        // One batch, so the temporary table lives through all statements.
        migrationBuilder.Sql("""
            CREATE TABLE #Merge (RunId uniqueidentifier NOT NULL PRIMARY KEY, RootId uniqueidentifier NOT NULL, [Shift] int NOT NULL);

            WITH Groups AS (
                SELECT r.Id,
                    FIRST_VALUE(r.Id) OVER (PARTITION BY r.WorkItemId, r.RepositoryFullName ORDER BY r.SysId) AS RootId,
                    SUM(r.Attempts) OVER (PARTITION BY r.WorkItemId, r.RepositoryFullName ORDER BY r.SysId
                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS [Shift],
                    SUM(CASE WHEN r.Status IN ('Pending', 'Queued', 'Running') THEN 1 ELSE 0 END)
                        OVER (PARTITION BY r.WorkItemId, r.RepositoryFullName) AS Active
                FROM AgentRuns r)
            INSERT INTO #Merge (RunId, RootId, [Shift])
            SELECT Id, RootId, ISNULL([Shift], 0) FROM Groups WHERE Id <> RootId AND Active = 0;

            UPDATE root SET
                root.Attempts = root.Attempts + agg.Attempts,
                root.MaxAttempts = root.Attempts + agg.Attempts + CASE WHEN latest.MaxAttempts > latest.Attempts THEN latest.MaxAttempts - latest.Attempts ELSE 0 END,
                root.NanoAiu = CASE WHEN root.NanoAiu IS NULL AND agg.NanoAiu IS NULL THEN NULL ELSE ISNULL(root.NanoAiu, 0) + ISNULL(agg.NanoAiu, 0) END,
                root.UsageComplete = CASE WHEN root.UsageComplete = 1 AND agg.Incomplete = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                root.PremiumRequests = CASE WHEN root.PremiumRequests IS NULL AND agg.PremiumRequests IS NULL THEN NULL
                    ELSE ISNULL(root.PremiumRequests, 0) + ISNULL(agg.PremiumRequests, 0) END,
                root.InputTokens = root.InputTokens + agg.InputTokens,
                root.OutputTokens = root.OutputTokens + agg.OutputTokens,
                root.CacheReadTokens = root.CacheReadTokens + agg.CacheReadTokens,
                root.CacheWriteTokens = root.CacheWriteTokens + agg.CacheWriteTokens,
                root.Status = latest.Status,
                root.Kind = latest.Kind,
                root.Error = latest.Error,
                root.RequestedModel = latest.RequestedModel,
                root.CompletedAt = latest.CompletedAt,
                root.LeaseUntil = NULL,
                root.Models = ISNULL(models.Models, root.Models),
                root.Summary = ISNULL(summary.Summary, root.Summary),
                root.Branch = ISNULL(pr.Branch, root.Branch),
                root.PullRequestNumber = ISNULL(pr.PullRequestNumber, root.PullRequestNumber),
                root.PullRequestUrl = ISNULL(pr.PullRequestUrl, root.PullRequestUrl),
                root.FeedbackUntil = feedback.FeedbackUntil
            FROM AgentRuns root
            JOIN (
                SELECT m.RootId,
                    SUM(r.Attempts) AS Attempts,
                    SUM(r.NanoAiu) AS NanoAiu,
                    MAX(CASE WHEN r.UsageComplete = 0 THEN 1 ELSE 0 END) AS Incomplete,
                    SUM(r.PremiumRequests) AS PremiumRequests,
                    SUM(r.InputTokens) AS InputTokens,
                    SUM(r.OutputTokens) AS OutputTokens,
                    SUM(r.CacheReadTokens) AS CacheReadTokens,
                    SUM(r.CacheWriteTokens) AS CacheWriteTokens
                FROM #Merge m
                JOIN AgentRuns r ON r.Id = m.RunId
                GROUP BY m.RootId) agg ON agg.RootId = root.Id
            CROSS APPLY (
                SELECT TOP 1 r.Status, r.Kind, r.Error, r.RequestedModel, r.CompletedAt, r.Attempts, r.MaxAttempts
                FROM #Merge m JOIN AgentRuns r ON r.Id = m.RunId
                WHERE m.RootId = root.Id
                ORDER BY r.SysId DESC) latest
            OUTER APPLY (
                SELECT TOP 1 r.Branch, r.PullRequestNumber, r.PullRequestUrl
                FROM AgentRuns r
                WHERE (r.Id = root.Id OR r.Id IN (SELECT m.RunId FROM #Merge m WHERE m.RootId = root.Id)) AND r.PullRequestNumber IS NOT NULL
                ORDER BY r.SysId DESC) pr
            OUTER APPLY (
                SELECT TOP 1 r.Summary
                FROM AgentRuns r
                WHERE (r.Id = root.Id OR r.Id IN (SELECT m.RunId FROM #Merge m WHERE m.RootId = root.Id)) AND r.Summary IS NOT NULL
                ORDER BY r.SysId DESC) summary
            OUTER APPLY (
                SELECT TOP 1 r.Models
                FROM AgentRuns r
                WHERE (r.Id = root.Id OR r.Id IN (SELECT m.RunId FROM #Merge m WHERE m.RootId = root.Id)) AND r.Models IS NOT NULL
                ORDER BY r.SysId DESC) models
            OUTER APPLY (
                -- How far the discussion of the task's pull request was handled, by any of the tasks that ran on it.
                SELECT MAX(ISNULL(r.FeedbackUntil, CASE WHEN r.Status = 'Succeeded' THEN r.CompletedAt END)) AS FeedbackUntil
                FROM AgentRuns r
                WHERE (r.Id = root.Id OR r.Id IN (SELECT m.RunId FROM #Merge m WHERE m.RootId = root.Id))
                    AND r.PullRequestNumber = ISNULL(pr.PullRequestNumber, root.PullRequestNumber)) feedback;

            UPDATE a SET a.AgentRunId = m.RootId, a.Attempt = a.Attempt + m.[Shift]
            FROM AgentRunAttempts a
            JOIN #Merge m ON m.RunId = a.AgentRunId;

            UPDATE u SET u.AgentRunId = m.RootId, u.Attempt = u.Attempt + m.[Shift]
            FROM AgentRunModelUsages u
            JOIN #Merge m ON m.RunId = u.AgentRunId;

            DELETE r
            FROM AgentRuns r
            JOIN #Merge m ON m.RunId = r.Id;

            DROP TABLE #Merge;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The folded tasks stay folded: their runs can't be told apart from the first task's own runs any more.
        migrationBuilder.DropColumn(
            name: "Kind",
            table: "AgentRunAttempts");
    }
}

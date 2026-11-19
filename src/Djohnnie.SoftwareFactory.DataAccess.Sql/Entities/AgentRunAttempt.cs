using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// One run (attempt) of a DGHSF agent worker task; see <see cref="Contracts.AgentRuns.AgentRunAttempt"/>. A child of its
/// <see cref="AgentRun"/>: the worker adds one each time it starts the task, so a retry is a new row next to the run that
/// failed. Deleting the task deletes its runs.
/// </summary>
public class AgentRunAttempt : Entity
{
    public Guid AgentRunId { get; set; }

    /// <summary>The number of this run within its task, starting at 1.</summary>
    public int Attempt { get; set; }

    public AgentRunStatus Status { get; set; }

    [MaxLength(500)]
    public string? Models { get; set; }

    [MaxLength(250)]
    public string? Branch { get; set; }

    public int? PullRequestNumber { get; set; }

    [MaxLength(500)]
    public string? PullRequestUrl { get; set; }

    [MaxLength(Contracts.AgentRuns.AgentRun.MaxSummaryLength)]
    public string? Summary { get; set; }

    [MaxLength(Contracts.AgentRuns.AgentRun.MaxErrorLength)]
    public string? Error { get; set; }

    public long? NanoAiu { get; set; }
    public bool UsageComplete { get; set; }
    public decimal? PremiumRequests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

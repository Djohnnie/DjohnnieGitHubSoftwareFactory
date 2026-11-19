using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// A run of the DGHSF agent worker; see <see cref="Contracts.AgentRuns.AgentRun"/>. Status changes are conditional updates
/// (<c>WHERE Status = expected</c>), so concurrent dispatchers and workers can't both claim a run. Like the background tasks
/// it refers to no other table: deleting a work item leaves its runs, which then only matter as history.
/// </summary>
public class AgentRun : Entity
{
    public Guid WorkItemId { get; set; }

    [Required, MaxLength(Repository.MaxFullNameLength)]
    public string RepositoryFullName { get; set; } = string.Empty;

    public AgentRunStatus Status { get; set; }
    public AgentRunKind Kind { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }

    [MaxLength(100)]
    public string? RequestedModel { get; set; }

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

    /// <summary>The pull request's discussion up to this time was handled by the task, which succeeded.</summary>
    public DateTimeOffset? FeedbackUntil { get; set; }

    /// <summary>Until when the worker that is running the agent owns the run; it renews this while the agent works.</summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

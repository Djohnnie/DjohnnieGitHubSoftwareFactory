using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>An epic, feature, user story, bug or task of one repository.</summary>
public class WorkItem : Entity
{
    public Guid RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;

    public WorkItemType Type { get; set; }

    /// <summary>The parent epic, feature or story; always in the same repository.</summary>
    public Guid? ParentId { get; set; }
    public WorkItem? Parent { get; set; }
    public List<WorkItem> Children { get; set; } = [];

    [Required, MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown; becomes the issue body.</summary>
    [MaxLength(Contracts.WorkItems.WorkItem.MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Markdown for the agent; becomes the first comment of the issue.</summary>
    [MaxLength(Contracts.WorkItems.WorkItem.MaxTechnicalDetailsLength)]
    public string TechnicalDetails { get; set; } = string.Empty;

    public WorkItemStatus Status { get; set; } = WorkItemStatus.Draft;

    /// <summary>Where the coding agent runs; stored as a number.</summary>
    public AgentExecutor Executor { get; set; }

    /// <summary>The custom agent id (file name without <c>.agent.md</c>), or <c>null</c> for the default Copilot agent.</summary>
    [MaxLength(100)]
    public string? CustomAgent { get; set; }

    /// <summary>The model for the agent assignment, or <c>null</c> for auto model selection.</summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>Branch Copilot branches from, or <c>null</c> for the default branch.</summary>
    [MaxLength(250)]
    public string? BaseBranch { get; set; }

    /// <summary>Extra instructions for Copilot, on top of the issue itself.</summary>
    [MaxLength(10_000)]
    public string? CustomInstructions { get; set; }

    /// <summary>Whether the DGHSF agent worker adds screenshots of the user interface it changed to the pull request.</summary>
    public bool IncludeScreenshots { get; set; } = true;

    /// <summary>Whether the sync starts a revision run of the DGHSF agent worker when Copilot code review left remarks.</summary>
    public bool ReviseOnCopilotReview { get; set; }

    /// <summary>Whether the sync asks the agent to fix the pull request's merge conflicts by itself.</summary>
    public bool FixMergeConflicts { get; set; } = true;

    public int? IssueNumber { get; set; }

    /// <summary>GitHub's issue id (not the number); the sub-issues API needs it.</summary>
    public long? IssueId { get; set; }

    [MaxLength(500)]
    public string? IssueUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>
    /// When the acceptance that is going on started (its claim, so the GitHub issue is created only once), or
    /// <c>null</c> when no acceptance is going on. A concurrency token, like <see cref="Status"/>.
    /// </summary>
    public DateTimeOffset? AcceptStartedAt { get; set; }

    /// <summary>
    /// When the earliest failed acceptance that may have created the issue anyway started, or <c>null</c>. The next
    /// attempt looks for the issue among the issues updated since then instead of creating a duplicate.
    /// </summary>
    public DateTimeOffset? AcceptIssueSince { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

    /// <summary>How far Copilot got, as of the last refresh from GitHub.</summary>
    public CopilotProgress Progress { get; set; }

    public int? PullRequestNumber { get; set; }

    [MaxLength(500)]
    public string? PullRequestUrl { get; set; }

    public PullRequestChecks PullRequestChecks { get; set; }

    public bool PullRequestHasConflicts { get; set; }

    [MaxLength(500)]
    public string? AgentRunUrl { get; set; }

    [MaxLength(250)]
    public string? PullRequestBranch { get; set; }

    /// <summary>
    /// The run (root background task) that generated the item from its repository, or <c>null</c>. Not a foreign key, so
    /// finished tasks can be cleaned up without touching the work items.
    /// </summary>
    public Guid? GeneratedByTaskId { get; set; }

    /// <summary>
    /// When feedback was last sent to the Copilot cloud agent, while a Copilot code review of its revision is still to be
    /// requested; <c>null</c> otherwise.
    /// </summary>
    public DateTimeOffset? CopilotRevisionRequestedAt { get; set; }

    /// <summary>When the latest Copilot code review was submitted that the sync acted on for <see cref="ReviseOnCopilotReview"/>.</summary>
    public DateTimeOffset? CopilotReviewHandledAt { get; set; }

    /// <summary>When the latest finished agent session of the Copilot cloud agent was created that the sync handled.</summary>
    public DateTimeOffset? CopilotSessionHandledAt { get; set; }

    /// <summary>When the agent was last asked to fix the pull request's merge conflicts; cleared once it no longer conflicts.</summary>
    public DateTimeOffset? MergeConflictFixRequestedAt { get; set; }
}

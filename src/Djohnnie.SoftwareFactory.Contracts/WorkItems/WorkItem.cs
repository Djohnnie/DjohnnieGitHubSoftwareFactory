using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>
/// A planning item (epic, feature, user story, bug or task) of one repository. Drafts only live in DGHSF;
/// accepting one creates a labelled GitHub issue, as a sub-issue of the parent's issue when there is one.
/// </summary>
public sealed record WorkItem
{
    public const int MaxDescriptionLength = 60_000;

    public Guid Id { get; set; }

    /// <summary>The repository the item belongs to, as <c>owner/name</c>.</summary>
    [Required, MaxLength(200)]
    public string RepositoryFullName { get; set; } = string.Empty;

    /// <summary>Set when the item is created; it cannot change afterwards.</summary>
    public WorkItemType Type { get; set; }

    /// <summary>The parent epic, feature or story in the same repository; see <see cref="WorkItemTypeRules"/>.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>The stored parent (read only; changing <see cref="ParentId"/> does not update it until saved).</summary>
    public WorkItemReference? Parent { get; set; }

    [Required, MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown; becomes the issue body.</summary>
    [MaxLength(MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional Markdown with technical details for the agent (e.g. files, APIs, constraints), kept out of the description.
    /// Accepting the item posts it as the first comment of the issue; it is never refreshed from GitHub.
    /// </summary>
    [MaxLength(MaxTechnicalDetailsLength)]
    public string TechnicalDetails { get; set; } = string.Empty;

    /// <summary>Leaves room for the comment's heading and marker within GitHub's 65,536 characters per comment.</summary>
    public const int MaxTechnicalDetailsLength = 60_000;

    public WorkItemStatus Status { get; set; } = WorkItemStatus.Draft;

    /// <summary>Where the coding agent runs: GitHub's Copilot cloud agent or the DGHSF agent worker.</summary>
    public AgentExecutor Executor { get; set; } = AgentExecutor.DghsfWorker;

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

    /// <summary>
    /// Whether the DGHSF agent worker runs the app and adds screenshots of the user interface it changed to the pull request
    /// (when the change has one). The Copilot cloud agent ignores it.
    /// </summary>
    public bool IncludeScreenshots { get; set; } = true;

    /// <summary>
    /// Whether the sync starts a revision run of the DGHSF agent worker by itself when Copilot code review has reviewed the
    /// worker's pull request with remarks. Only for the DGHSF agent worker; the store clears it for the Copilot cloud agent.
    /// </summary>
    public bool ReviseOnCopilotReview { get; set; }

    public int? IssueNumber { get; set; }

    /// <summary>GitHub's issue id (not the number); the sub-issues API needs it.</summary>
    public long? IssueId { get; set; }

    [MaxLength(500)]
    public string? IssueUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

    /// <summary>How far Copilot got, as of the last refresh from GitHub.</summary>
    public CopilotProgress Progress { get; set; }

    /// <summary>The pull request linked to the issue that the progress is about.</summary>
    public int? PullRequestNumber { get; set; }

    [MaxLength(500)]
    public string? PullRequestUrl { get; set; }

    public PullRequestChecks PullRequestChecks { get; set; }

    /// <summary>The Actions run of Copilot's latest agent session (its logs).</summary>
    [MaxLength(500)]
    public string? AgentRunUrl { get; set; }

    /// <summary>The pull request's head branch, to check it out locally.</summary>
    [MaxLength(250)]
    public string? PullRequestBranch { get; set; }

    /// <summary>The run (root background task) that generated the item from its repository, or <c>null</c>.</summary>
    public Guid? GeneratedByTaskId { get; set; }

    /// <summary>
    /// When feedback was last sent to the Copilot cloud agent on the pull request, while the sync still has to ask Copilot
    /// code review to review the revision once Copilot's agent session for it has finished; <c>null</c> otherwise. Only
    /// sending feedback and the sync change it; saving the item leaves it as it is.
    /// </summary>
    public DateTimeOffset? CopilotRevisionRequestedAt { get; set; }

    /// <summary>
    /// When the latest Copilot code review was submitted that the sync acted on for <see cref="ReviseOnCopilotReview"/> (it
    /// started a revision run or gave up on it), so it acts on every review once; <c>null</c> before the first. Only the sync
    /// changes it; saving the item leaves it as it is.
    /// </summary>
    public DateTimeOffset? CopilotReviewHandledAt { get; set; }

    /// <summary>A draft, also one the background agents generated (<see cref="WorkItemStatus.Generated"/>).</summary>
    public bool IsDraft => Status.IsDraft;

    /// <summary>A draft the background agents generated from the repository that hasn't been accepted yet.</summary>
    public bool IsGenerated
    {
        get
        {
            return Status == WorkItemStatus.Generated;
        }
    }

    /// <summary>Whether the item was accepted and has a GitHub issue it can be refreshed from.</summary>
    public bool HasIssue => IssueNumber is not null;

    /// <summary>Whether the assignment settings can still be changed (only for types that are assigned to an agent).</summary>
    public bool CanEditAssignment => Type.CanBeAssigned && Status is WorkItemStatus.Draft or WorkItemStatus.Generated or WorkItemStatus.Accepted;

    public WorkItem Copy()
    {
        return this with { };
    }
}

/// <summary>A short reference to another work item, e.g. a parent or child.</summary>
public sealed record WorkItemReference(Guid Id, WorkItemType Type, string Title, WorkItemStatus Status, int? IssueNumber, string? IssueUrl);

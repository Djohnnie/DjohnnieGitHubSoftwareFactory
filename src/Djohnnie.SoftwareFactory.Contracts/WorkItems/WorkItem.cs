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

    public WorkItemStatus Status { get; set; } = WorkItemStatus.Draft;

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

    public int? IssueNumber { get; set; }

    /// <summary>GitHub's issue id (not the number); the sub-issues API needs it.</summary>
    public long? IssueId { get; set; }

    [MaxLength(500)]
    public string? IssueUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

    public bool IsDraft => Status == WorkItemStatus.Draft;

    /// <summary>Whether the item was accepted and has a GitHub issue it can be refreshed from.</summary>
    public bool HasIssue => IssueNumber is not null;

    /// <summary>Whether the assignment settings can still be changed (only for types that are assigned to an agent).</summary>
    public bool CanEditAssignment => Type.CanBeAssigned && Status is WorkItemStatus.Draft or WorkItemStatus.Accepted;

    public WorkItem Copy()
    {
        return this with { };
    }
}

/// <summary>A short reference to another work item, e.g. a parent or child.</summary>
public sealed record WorkItemReference(Guid Id, WorkItemType Type, string Title, WorkItemStatus Status, int? IssueNumber, string? IssueUrl);

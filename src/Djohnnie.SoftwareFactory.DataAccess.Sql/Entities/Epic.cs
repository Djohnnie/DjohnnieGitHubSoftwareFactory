using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.Epics;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>A planning item at the top of the epic → feature → story hierarchy.</summary>
public class Epic
{
    public const string Label = "epic";
    public const string LabelColor = "6F42C1";
    public const int MaxDescriptionLength = 60_000;

    public int Id { get; set; }

    /// <summary>The repository the epic belongs to, as <c>owner/name</c>.</summary>
    [Required, MaxLength(200)]
    public string RepositoryFullName { get; set; } = string.Empty;

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

    [MaxLength(500)]
    public string? IssueUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

}

using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;

/// <summary>A reusable title example and Markdown skeleton for new work items of one type, shared across repositories.</summary>
public sealed record WorkItemTemplate
{
    public int Id { get; set; }

    /// <summary>Set when the template is created; it cannot change afterwards.</summary>
    public WorkItemType Type { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string TitleTemplate { get; set; } = string.Empty;

    [Required, MaxLength(WorkItem.MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;

/// <summary>A reusable title example and Markdown skeleton for new work items of one type, shared across repositories.</summary>
public sealed record WorkItemTemplate
{
    public Guid Id { get; set; }

    /// <summary>Set when the template is created; it cannot change afterwards.</summary>
    public WorkItemType Type { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string TitleTemplate { get; set; } = string.Empty;

    [Required, MaxLength(WorkItem.MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether this is the default template of its type; saving one as default clears the previous one.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The template to use for new items: the default one, otherwise the first of <paramref name="templates"/>.</summary>
    public static WorkItemTemplate? Proposed(IReadOnlyList<WorkItemTemplate> templates)
    {
        return templates.FirstOrDefault(t => t.IsDefault) ?? templates.FirstOrDefault();
    }
}

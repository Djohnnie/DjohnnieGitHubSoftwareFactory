using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

public class WorkItemTemplate
{
    public int Id { get; set; }

    public WorkItemType Type { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string TitleTemplate { get; set; } = string.Empty;

    [Required, MaxLength(Contracts.WorkItems.WorkItem.MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

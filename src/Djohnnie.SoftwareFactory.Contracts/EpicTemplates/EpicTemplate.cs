using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.Epics;

namespace Djohnnie.SoftwareFactory.Contracts.EpicTemplates;

public sealed record EpicTemplate
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string TitleTemplate { get; set; } = string.Empty;

    [Required, MaxLength(Epic.MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

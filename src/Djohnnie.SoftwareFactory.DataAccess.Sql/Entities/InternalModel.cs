using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>A Microsoft Foundry model the internal agents can run on, with its current price per 1 million tokens.</summary>
public class InternalModel : Entity
{
    [Required, MaxLength(Contracts.InternalModels.InternalModel.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(Contracts.InternalModels.InternalModel.MaxLabelLength)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(Contracts.InternalModels.InternalModel.MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
    public decimal? CachedInputPricePerMillion { get; set; }
    public bool IsFlagship { get; set; }
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

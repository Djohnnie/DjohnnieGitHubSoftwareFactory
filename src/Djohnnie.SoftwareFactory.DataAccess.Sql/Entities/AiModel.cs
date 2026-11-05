using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// An AI model that can be used by custom agents. Prices are in USD per 1 million tokens.
/// </summary>
public class AiModel
{
    public const string DefaultTier = "Default";

    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The value written to the <c>model</c> frontmatter key of an agent file.</summary>
    [Required, MaxLength(100)]
    public string ModelId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Provider { get; set; }

    [MaxLength(50)]
    public string? ReleaseStatus { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    [Required, MaxLength(50)]
    public string Tier { get; set; } = DefaultTier;

    [MaxLength(50)]
    public string? Threshold { get; set; }

    public decimal? InputPrice { get; set; }
    public decimal? CachedInputPrice { get; set; }
    public decimal? CacheWritePrice { get; set; }
    public decimal? OutputPrice { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsFavorite { get; set; }

    [MaxLength(500)]
    public string? SourceUrl { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

}

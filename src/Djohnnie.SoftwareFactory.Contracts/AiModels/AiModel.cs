using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.Contracts.AiModels;

/// <summary>
/// An AI model that can be used by custom agents. Prices are in USD per 1 million tokens.
/// </summary>
public sealed record AiModel
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

    [MaxLength(500)]
    public string? SourceUrl { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDefaultTier => string.Equals(Tier, DefaultTier, StringComparison.OrdinalIgnoreCase);

    public string CostSummary => FormatCost(InputPrice, OutputPrice);

    public static string FormatCost(decimal? input, decimal? output) =>
        input is null && output is null
            ? "No pricing"
            : $"{FormatPrice(input)} in / {FormatPrice(output)} out per 1M tokens";

    public static string FormatPrice(decimal? price) =>
        price is null ? "–" : price.Value.ToString(price.Value < 0.1m && price.Value != 0 ? "$0.000" : "$0.00", System.Globalization.CultureInfo.InvariantCulture);

    public AiModel Copy() => this with { };
}

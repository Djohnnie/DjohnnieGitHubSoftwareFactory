using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.Contracts.AiModels;

/// <summary>
/// An AI model that can be used by custom agents. Prices are in GitHub AI credits per 1 million tokens
/// (1 AI credit = 0.01 USD); importing them from GitHub's docs converts the listed US dollar amounts to credits.
/// </summary>
public sealed record AiModel
{
    public const string DefaultTier = "Default";

    /// <summary>The number of GitHub AI credits in one US dollar.</summary>
    public const decimal CreditsPerUsd = 100m;

    public Guid Id { get; set; }

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

    /// <summary>
    /// The running totals of what DGHSF agent worker sessions spent on this model. Kept by the store when a run ends; saving
    /// the model never changes them.
    /// </summary>
    public AiModelUsageTotals Usage { get; set; } = AiModelUsageTotals.None;

    public bool IsDefaultTier => string.Equals(Tier, DefaultTier, StringComparison.OrdinalIgnoreCase);

    public string CostSummary => FormatCost(InputPrice, OutputPrice);

    public static string FormatCost(decimal? input, decimal? output)
    {
        return input is null && output is null
            ? "No pricing"
            : $"{FormatCredits(input)} in / {FormatCredits(output)} out per 1M tokens";
    }

    /// <summary>Converts a US dollar amount to GitHub AI credits.</summary>
    public static decimal? UsdToCredits(decimal? usd)
    {
        return usd * CreditsPerUsd;
    }

    /// <summary>Formats an amount of GitHub AI credits, e.g. "1,400 AI credits".</summary>
    public static string FormatCredits(decimal? credits)
    {
        return credits is null ? "–" : $"{credits.Value.ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture)} AI credits";
    }

    /// <summary>Formats a euro amount, used for the Microsoft Foundry models DGHSF itself runs on.</summary>
    public static string FormatPrice(decimal? price)
    {
        return price is null ? "–" : price.Value.ToString("€#,0.000", System.Globalization.CultureInfo.InvariantCulture);
    }

    public AiModel Copy()
    {
        return this with { };
    }
}

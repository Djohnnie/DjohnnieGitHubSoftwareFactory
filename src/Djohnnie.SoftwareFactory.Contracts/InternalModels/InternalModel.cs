using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.Contracts.InternalModels;

/// <summary>
/// A Microsoft Foundry model DGHSF's own agents can run on, with its price in euro per 1 million tokens.
/// Separate from the AI models of GitHub Copilot custom agents. Shared across repositories.
/// </summary>
public sealed record InternalModel
{
    public const int MaxNameLength = 200;
    public const int MaxLabelLength = 100;
    public const int MaxDescriptionLength = 1000;
    public const decimal MaxPrice = 1_000_000m;

    /// <summary>The number of decimals a cost is stored with.</summary>
    public const int CostDecimals = 8;

    public Guid Id { get; set; }

    /// <summary>The model or deployment name on the configured endpoint.</summary>
    [Required, MaxLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The human-readable name of the model, on one line.</summary>
    [Required, MaxLength(MaxLabelLength)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }

    /// <summary>The price of input tokens read from the cache; empty when they cost the same as other input tokens.</summary>
    public decimal? CachedInputPricePerMillion { get; set; }

    /// <summary>Marks a flagship model; any number of models can be one.</summary>
    public bool IsFlagship { get; set; }

    /// <summary>The model new agents get; at most one model is the default.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool HasPrice => InputPricePerMillion > 0 || OutputPricePerMillion > 0;

    public static decimal CostOf(long tokens, decimal pricePerMillion)
    {
        return Math.Round(tokens * pricePerMillion / 1_000_000m, CostDecimals);
    }

    /// <summary>The cost of the input of a request; <paramref name="cachedInputTokens"/> are part of <paramref name="inputTokens"/>.</summary>
    public static decimal InputCostOf(long inputTokens, long cachedInputTokens, decimal pricePerMillion, decimal cachedPricePerMillion)
    {
        var cached = Math.Clamp(cachedInputTokens, 0, inputTokens);
        return Math.Round(((inputTokens - cached) * pricePerMillion + cached * cachedPricePerMillion) / 1_000_000m, CostDecimals);
    }

    public InternalModel Copy()
    {
        return this with { };
    }
}

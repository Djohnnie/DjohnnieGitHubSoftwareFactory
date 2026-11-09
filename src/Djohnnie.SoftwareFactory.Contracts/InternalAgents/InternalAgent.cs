using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>
/// One of DGHSF's own (built-in) agents. The set of agents is fixed in code and seeded into the database;
/// only the system prompt, the model and whether the agent is enabled can be changed. Shared across repositories.
/// </summary>
public sealed record InternalAgent
{
    public const int MaxInstructionsLength = 30_000;

    public Guid Id { get; set; }

    /// <summary>The fixed identifier, see <see cref="InternalAgentKeys"/>.</summary>
    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public InternalAgentKind Kind { get; set; }

    /// <summary>The system prompt.</summary>
    [Required, MaxLength(MaxInstructionsLength)]
    public string Instructions { get; set; } = string.Empty;

    /// <summary>The system prompt DGHSF proposes (read only).</summary>
    public string DefaultInstructions { get; set; } = string.Empty;

    /// <summary>The internal model the agent runs on. Empty until it is set: the agent then uses the configured model.</summary>
    public Guid? ModelId { get; set; }

    /// <summary>The name of the model of <see cref="ModelId"/> (read only); empty when the agent has none.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>The label of the model of <see cref="ModelId"/> (read only); empty when the agent has none.</summary>
    public string ModelLabel { get; set; } = string.Empty;

    /// <summary>Disabled Copilots are hidden; disabled background agents don't run.</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // The running totals (read only): each request adds what it cost at that moment, so later price changes don't affect them.
    public long RequestCount { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }

    /// <summary>The part of <see cref="TotalInputTokens"/> that was read from the cache.</summary>
    public long TotalCachedInputTokens { get; set; }

    /// <summary>The part of <see cref="TotalOutputTokens"/> used for reasoning; for information, it is already in the output cost.</summary>
    public long TotalReasoningTokens { get; set; }

    public decimal TotalInputCost { get; set; }
    public decimal TotalOutputCost { get; set; }

    public decimal TotalCost => TotalInputCost + TotalOutputCost;

    public InternalAgentUsageTotals UsageTotals => new(RequestCount, TotalInputTokens, TotalOutputTokens, TotalCachedInputTokens, TotalReasoningTokens, TotalInputCost, TotalOutputCost);

    public bool IsCustomized => !string.Equals(Normalize(Instructions), Normalize(DefaultInstructions), StringComparison.Ordinal);

    public InternalAgent Copy()
    {
        return this with { };
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }
}

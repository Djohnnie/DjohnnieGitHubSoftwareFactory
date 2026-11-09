using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>One of DGHSF's own agents; seeded from code, with an editable system prompt.</summary>
public class InternalAgent : Entity
{
    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public InternalAgentKind Kind { get; set; }

    [Required, MaxLength(Contracts.InternalAgents.InternalAgent.MaxInstructionsLength)]
    public string Instructions { get; set; } = string.Empty;

    public Guid? InternalModelId { get; set; }
    public InternalModel? InternalModel { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The number of requests so far; also the concurrency token that keeps simultaneous requests from losing an update.</summary>
    public long RequestCount { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCachedInputTokens { get; set; }
    public long TotalReasoningTokens { get; set; }
    public decimal TotalInputCost { get; set; }
    public decimal TotalOutputCost { get; set; }
}

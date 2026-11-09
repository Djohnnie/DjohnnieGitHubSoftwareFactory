using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// One request of an internal agent to its model. The model is copied by name, without a foreign key, and the prices and
/// costs are those of that moment, so the row stays correct when the model changes or is deleted.
/// </summary>
public class InternalAgentUsage : Entity
{
    public Guid InternalAgentId { get; set; }
    public InternalAgent InternalAgent { get; set; } = default!;

    public InternalAgentUsageSource Source { get; set; }

    [Required, MaxLength(Contracts.InternalModels.InternalModel.MaxNameLength)]
    public string Model { get; set; } = string.Empty;

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedInputTokens { get; set; }

    /// <summary>The part of <see cref="OutputTokens"/> used for reasoning; for information, it is already in <see cref="OutputCost"/>.</summary>
    public long ReasoningTokens { get; set; }

    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }

    /// <summary>What the cached input tokens were charged at: the model's cached price, or else its input price.</summary>
    public decimal CachedInputPricePerMillion { get; set; }

    public decimal InputCost { get; set; }
    public decimal OutputCost { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

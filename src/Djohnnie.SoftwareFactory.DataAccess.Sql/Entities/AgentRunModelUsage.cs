using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// What one model cost in one session (attempt) of a DGHSF agent worker run; see <see cref="Contracts.AiModels.AiModelUsage"/>.
/// The model is copied by name; <see cref="AiModelId"/> links the row to the catalog entry whose running totals it was added
/// to, and becomes <c>null</c> when that entry is deleted.
/// </summary>
public class AgentRunModelUsage : Entity
{
    public Guid AgentRunId { get; set; }

    public Guid? AiModelId { get; set; }

    public int Attempt { get; set; }

    [Required, MaxLength(100)]
    public string Model { get; set; } = string.Empty;

    public long NanoAiu { get; set; }
    public bool Complete { get; set; }
    public int Requests { get; set; }
    public decimal PremiumRequests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

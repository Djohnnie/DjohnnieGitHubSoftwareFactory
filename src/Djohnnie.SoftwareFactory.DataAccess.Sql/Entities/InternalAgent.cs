using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>One of DGHSF's own agents; seeded from code, with an editable system prompt.</summary>
public class InternalAgent
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public InternalAgentKind Kind { get; set; }

    [Required, MaxLength(Contracts.InternalAgents.InternalAgent.MaxInstructionsLength)]
    public string Instructions { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

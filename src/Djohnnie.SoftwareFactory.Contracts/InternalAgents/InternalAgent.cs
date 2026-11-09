using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>
/// One of DGHSF's own (built-in) agents. The set of agents is fixed in code and seeded into the database;
/// only the system prompt, the model of a Copilot and whether the agent is enabled can be changed. Shared across repositories.
/// </summary>
public sealed record InternalAgent
{
    public const int MaxInstructionsLength = 30_000;
    public const int MaxModelLength = 200;

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

    /// <summary>The model (or deployment) name of a Copilot. Empty until it is set: the agent then uses the configured model.</summary>
    [MaxLength(MaxModelLength)]
    public string Model { get; set; } = string.Empty;

    /// <summary>Disabled Copilots are hidden; disabled background agents don't run.</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

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

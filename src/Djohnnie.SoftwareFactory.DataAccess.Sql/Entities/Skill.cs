using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// A GitHub Copilot agent skill, managed globally. Its properties are stored here; enabling it in a repository
/// commits them as <c>.github/skills/{Name}/SKILL.md</c>.
/// </summary>
public class Skill : Entity
{
    /// <summary>The skill name and directory name; unique across skills.</summary>
    [Required, MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(1_024)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? License { get; set; }

    [MaxLength(500)]
    public string? Compatibility { get; set; }

    /// <summary>The space-separated <c>allowed-tools</c>.</summary>
    [MaxLength(1_000)]
    public string? AllowedTools { get; set; }

    /// <summary>The Markdown instructions below the frontmatter.</summary>
    [Required]
    public string Instructions { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

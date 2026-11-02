using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// A reusable custom agent profile, stored in the app's database instead of a repository.
/// It is used to create the same agent in one or more repositories.
/// </summary>
public class AgentTemplate
{
    public int Id { get; set; }

    /// <summary>The default agent file name (without <c>.agent.md</c>); unique across templates.</summary>
    [Required, MaxLength(100)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>The display name: the agent's name, or the file name when it has none. Kept for listing.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The agent's description. Kept for listing.</summary>
    [MaxLength(2_000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>The complete agent file: YAML frontmatter plus the Markdown prompt.</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

}

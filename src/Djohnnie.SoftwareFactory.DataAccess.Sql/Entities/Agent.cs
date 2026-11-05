using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// Local data about a custom agent of a repository. The agent itself lives in the repository
/// (<c>.github/agents/{FileName}.agent.md</c>); this row only keeps what GitHub does not, such as the favorite flag.
/// </summary>
public sealed class Agent
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;

    /// <summary>The agent id: the file name without <c>.agent.md</c>.</summary>
    [Required, MaxLength(100)]
    public string FileName { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>A mind map of one repository. Its ideas are always saved together, so they are stored as one JSON document.</summary>
public class Mindmap : Entity
{
    public Guid RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;

    /// <summary>A copy of the central idea, so the list of mindmaps doesn't need the nodes.</summary>
    [Required, MaxLength(Contracts.Mindmaps.Mindmap.MaxTextLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>The ideas as a JSON array (see <c>MindmapStore</c>).</summary>
    [Required]
    public string Nodes { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

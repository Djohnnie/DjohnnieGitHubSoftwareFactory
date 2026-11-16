using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>A virtual folder of the knowledge base; it belongs to one repository and holds that repository's files.</summary>
public class KnowledgeFolder : Entity
{
    public Guid RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;

    /// <summary>A folder of the same repository, or <c>null</c> at the top of the repository's library.</summary>
    public Guid? ParentId { get; set; }
    public KnowledgeFolder? Parent { get; set; }

    /// <summary>Unique among the folders of the repository with the same parent.</summary>
    [Required, MaxLength(Contracts.KnowledgeBase.KnowledgeFolder.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

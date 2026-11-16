using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>A file of the knowledge base; its content is the blob named after <see cref="Entity.Id"/>.</summary>
public class KnowledgeFile : Entity
{
    public Guid? FolderId { get; set; }
    public KnowledgeFolder? Folder { get; set; }

    /// <summary>The repository the file belongs to; <c>null</c> for a global file.</summary>
    public Guid? RepositoryId { get; set; }
    public Repository? Repository { get; set; }

    [Required, MaxLength(Contracts.KnowledgeBase.KnowledgeFile.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(Contracts.KnowledgeBase.KnowledgeFile.MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    [Required, MaxLength(Contracts.KnowledgeBase.KnowledgeFile.MaxFileNameLength)]
    public string FileName { get; set; } = string.Empty;

    [Required, MaxLength(Contracts.KnowledgeBase.KnowledgeFile.MaxContentTypeLength)]
    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

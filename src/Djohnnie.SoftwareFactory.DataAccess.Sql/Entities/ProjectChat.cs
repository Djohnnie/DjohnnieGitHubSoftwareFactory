using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// A conversation with the DGHSF Agent on the chat page. A repository has any number of them, listed by when they were
/// last used.
/// </summary>
public sealed class ProjectChat : Entity
{
    public Guid RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;

    [Required, MaxLength(Contracts.ProjectChats.ProjectChat.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the last message was added; the creation time until then.</summary>
    public DateTimeOffset LastUsedAt { get; set; }

    public List<ProjectChatMessage> Messages { get; set; } = [];
}

using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// A conversation with an internal Copilot. It belongs to exactly one owner, which is enforced by a check constraint;
/// an owner has at most one open conversation.
/// </summary>
public sealed class ChatConversation : Entity
{
    /// <summary>The internal agent the conversation was started with.</summary>
    [Required, MaxLength(100)]
    public string CopilotKey { get; set; } = string.Empty;

    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }

    public Guid? WorkItemTemplateId { get; set; }
    public WorkItemTemplate? WorkItemTemplate { get; set; }

    public Guid? AgentTemplateId { get; set; }
    public AgentTemplate? AgentTemplate { get; set; }

    public Guid? AgentId { get; set; }
    public Agent? Agent { get; set; }

    /// <summary>Set for the conversation about a repository's Copilot instructions.</summary>
    public Guid? RepositoryId { get; set; }
    public Repository? Repository { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When the user started a new conversation instead, or <c>null</c> while this one is open.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    public List<ChatMessage> Messages { get; set; } = [];
}

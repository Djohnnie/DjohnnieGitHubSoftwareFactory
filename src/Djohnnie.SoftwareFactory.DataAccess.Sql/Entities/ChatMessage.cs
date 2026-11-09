using Djohnnie.SoftwareFactory.Contracts.Assistant;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>One message of a Copilot conversation.</summary>
public sealed class ChatMessage : Entity
{
    public Guid ConversationId { get; set; }
    public ChatConversation Conversation { get; set; } = null!;

    /// <summary>The position in the conversation; unique within it.</summary>
    public int Sequence { get; set; }

    public ChatMessageRole Role { get; set; }

    /// <summary>What the chat shows; <c>null</c> for messages only the model sees, such as tool calls and results.</summary>
    public string? DisplayText { get; set; }

    /// <summary>The serialized message replayed to the model; <c>null</c> for messages only the chat shows.</summary>
    public string? Payload { get; set; }

    public bool IsError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

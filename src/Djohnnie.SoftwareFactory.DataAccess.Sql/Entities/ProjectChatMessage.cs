using Djohnnie.SoftwareFactory.Contracts.Assistant;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// One message of a conversation with the DGHSF Agent, or a summary of the conversation up to it. The model gets the last
/// summary and the messages that aren't summarized; the chat shows every message but no summary.
/// </summary>
public sealed class ProjectChatMessage : Entity
{
    public Guid ChatId { get; set; }
    public ProjectChat Chat { get; set; } = null!;

    /// <summary>The position in the conversation; unique within it.</summary>
    public int Sequence { get; set; }

    public ChatMessageRole Role { get; set; }

    /// <summary>
    /// What the chat shows; <c>null</c> for messages only the model sees, such as tool calls and results. For a summary:
    /// the summary text, which the chat doesn't show.
    /// </summary>
    public string? DisplayText { get; set; }

    /// <summary>The serialized message replayed to the model; <c>null</c> for messages only the chat shows.</summary>
    public string? Payload { get; set; }

    public bool IsError { get; set; }

    /// <summary>Whether a (later) summary covers this message, so it is no longer replayed to the model.</summary>
    public bool IsSummarized { get; set; }

    /// <summary>Whether this is a summary of the conversation so far, written by the Chat Summarizer.</summary>
    public bool IsSummary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

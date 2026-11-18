using Djohnnie.SoftwareFactory.Contracts.Assistant;

namespace Djohnnie.SoftwareFactory.Contracts.ProjectChats;

/// <summary>A conversation with the DGHSF Agent about the whole project of one repository, as listed on the chat page.</summary>
/// <param name="LastUsedAt">When the last message was added; the creation time until then.</param>
public sealed record ProjectChatSummary(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt);

/// <summary>A conversation with what the chat shows of its messages, in order.</summary>
public sealed record ProjectChat(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, IReadOnlyList<ChatDisplayMessage> Messages)
{
    public const int MaxTitleLength = 100;

    /// <summary>The title until the Chat Title Writer has named the conversation, or when it can't.</summary>
    public const string DefaultTitle = "New chat";

    /// <summary>
    /// The number of chat messages (what the chat shows: the user's messages and the replies) after which the Chat
    /// Summarizer summarizes the conversation so far.
    /// </summary>
    public const int SummaryInterval = 10;

    /// <summary>The longest summary that is stored.</summary>
    public const int MaxSummaryLength = 8_000;

    public ProjectChatSummary Summary()
    {
        return new(Id, Title, CreatedAt, LastUsedAt);
    }
}

public sealed record StartProjectChatRequest(string RepositoryFullName, string Title);

public sealed record AppendProjectChatMessagesRequest(string RepositoryFullName, Guid Id, IReadOnlyList<StoredChatMessage> Messages);

public sealed record RenameProjectChatRequest(string RepositoryFullName, Guid Id, string Title);

/// <summary>What the Chat Summarizer gets to summarize: the last summary and the chat messages after it.</summary>
/// <param name="PreviousSummary">The last summary; <c>null</c> before the first one.</param>
/// <param name="Messages">The chat messages that aren't summarized yet, in order, without the ones that failed.</param>
/// <param name="UpToSequence">The last message the new summary covers, including tool calls and results.</param>
public sealed record ProjectChatHistoryToSummarize(string? PreviousSummary, IReadOnlyList<ChatDisplayMessage> Messages, int UpToSequence);

/// <param name="Summary">The summary of the whole conversation up to and including <paramref name="UpToSequence"/>.</param>
/// <param name="Payload">The summary as the serialized message replayed to the model instead of the messages it covers.</param>
public sealed record SaveProjectChatSummaryRequest(string RepositoryFullName, Guid Id, string Summary, string Payload, int UpToSequence);

/// <param name="Summarized">Whether the Chat Summarizer stored a new summary.</param>
public sealed record ProjectChatSummarizeResult(bool Summarized);

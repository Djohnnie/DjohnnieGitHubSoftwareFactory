using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

/// <summary>The conversations with the DGHSF Agent; every operation is scoped to one repository.</summary>
public interface IProjectChatStore
{
    /// <summary>The repository's conversations, most recently used first.</summary>
    Task<List<ProjectChatSummary>> GetAllAsync(string repositoryFullName, CancellationToken cancellationToken = default);

    /// <summary>
    /// The conversation with what the chat shows of it (every message, also the summarized ones, but no summary), or
    /// <c>null</c> when the repository has no such conversation.
    /// </summary>
    Task<ProjectChat?> GetAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The serialized messages to replay to the model: the last summary, then the messages it doesn't cover, in order;
    /// empty when there is no such conversation.
    /// </summary>
    Task<List<string>> GetPayloadsAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Starts an empty conversation.</summary>
    Task<ProjectChatSummary> StartAsync(string repositoryFullName, string title, CancellationToken cancellationToken = default);

    /// <summary>Adds the messages, all or nothing, and marks the conversation as used now.</summary>
    Task<ProjectChatSummary> AppendAsync(string repositoryFullName, Guid id, IReadOnlyList<StoredChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// The last summary and the messages after it, once at least <paramref name="minMessages"/> chat messages aren't
    /// summarized; <c>null</c> before that or when there is no such conversation.
    /// </summary>
    Task<ProjectChatHistoryToSummarize?> GetHistoryToSummarizeAsync(string repositoryFullName, Guid id, int minMessages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the summary and marks everything up to <paramref name="upToSequence"/> (messages and older summaries) as
    /// summarized, all or nothing. Doesn't change when the conversation was last used.
    /// </summary>
    Task SaveSummaryAsync(string repositoryFullName, Guid id, string summary, string payload, int upToSequence, CancellationToken cancellationToken = default);

    /// <summary>Changes the title; doesn't change when the conversation was last used.</summary>
    Task<ProjectChatSummary> RenameAsync(string repositoryFullName, Guid id, string title, CancellationToken cancellationToken = default);

    /// <summary>Deletes the conversation with its messages; nothing happens when it doesn't exist.</summary>
    Task DeleteAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default);
}

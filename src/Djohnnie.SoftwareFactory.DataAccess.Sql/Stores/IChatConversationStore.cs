using Djohnnie.SoftwareFactory.Contracts.Assistant;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

/// <summary>The Copilot conversations; each owner has at most one open conversation.</summary>
public interface IChatConversationStore
{
    /// <summary>What the chat shows of the open conversation, in order; empty when there is none.</summary>
    Task<List<ChatDisplayMessage>> GetDisplayMessagesAsync(ChatOwner owner, CancellationToken cancellationToken = default);

    /// <summary>The serialized messages of the open conversation to replay to the model, in order.</summary>
    Task<List<string>> GetPayloadsAsync(ChatOwner owner, CancellationToken cancellationToken = default);

    /// <summary>Adds the messages to the open conversation, starting one when there is none, all or nothing.</summary>
    Task AppendAsync(ChatOwner owner, string copilotKey, IReadOnlyList<StoredChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Closes the open conversation, if any; it stays stored but is no longer shown.</summary>
    Task CloseAsync(ChatOwner owner, CancellationToken cancellationToken = default);
}

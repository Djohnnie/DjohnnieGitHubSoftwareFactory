using Djohnnie.SoftwareFactory.Contracts.Agents;

namespace Djohnnie.SoftwareFactory.Contracts.Assistant;

public enum ChatOwnerKind
{
    WorkItem,
    WorkItemTemplate,
    AgentTemplate,
    Agent,
    CopilotInstructions
}

public enum ChatMessageRole
{
    User,
    Assistant,
    Tool
}

/// <summary>What a Copilot conversation belongs to; it is shown again when that page is opened again.</summary>
public sealed record ChatOwner
{
    private ChatOwner(ChatOwnerKind kind, string? repositoryFullName = null, Guid? id = null, string? fileName = null)
    {
        Kind = kind;
        RepositoryFullName = repositoryFullName;
        Id = id;
        FileName = fileName;
    }

    public ChatOwnerKind Kind { get; }
    public string? RepositoryFullName { get; }
    public Guid? Id { get; }
    public string? FileName { get; }

    public static ChatOwner ForWorkItem(string repositoryFullName, Guid id)
    {
        return new(ChatOwnerKind.WorkItem, repositoryFullName, id);
    }

    public static ChatOwner ForWorkItemTemplate(Guid id)
    {
        return new(ChatOwnerKind.WorkItemTemplate, id: id);
    }

    public static ChatOwner ForAgentTemplate(Guid id)
    {
        return new(ChatOwnerKind.AgentTemplate, id: id);
    }

    /// <param name="repositoryFullName">The repository of the agent.</param>
    /// <param name="fileName">The agent's file name, with or without <c>.agent.md</c>.</param>
    public static ChatOwner ForAgent(string repositoryFullName, string fileName)
    {
        return new(ChatOwnerKind.Agent, repositoryFullName, fileName: AgentDefinition.GetFileName(fileName));
    }

    public static ChatOwner ForCopilotInstructions(string repositoryFullName)
    {
        return new(ChatOwnerKind.CopilotInstructions, repositoryFullName);
    }
}

/// <summary>A message as the chat shows it.</summary>
public sealed record ChatDisplayMessage(ChatMessageRole Role, string Text, bool IsError);

/// <summary>A message to store.</summary>
/// <param name="Role">Who wrote the message.</param>
/// <param name="DisplayText">What the chat shows; <c>null</c> for messages only the model sees, such as tool calls.</param>
/// <param name="Payload">The serialized message replayed to the model; <c>null</c> for messages only the chat shows.</param>
/// <param name="IsError">The message reports a failure.</param>
public sealed record StoredChatMessage(ChatMessageRole Role, string? DisplayText, string? Payload, bool IsError = false);

public sealed record ChatOwnerRequest(ChatOwner Owner);

public sealed record AppendChatMessagesRequest(ChatOwner Owner, string CopilotKey, IReadOnlyList<StoredChatMessage> Messages);

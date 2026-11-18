using Djohnnie.SoftwareFactory.Application.Features.Assistant;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;
using Djohnnie.SoftwareFactory.Application.Features.Mindmaps;
using Djohnnie.SoftwareFactory.Application.Features.Skills;
using Djohnnie.SoftwareFactory.Application.Features.SyncLog;
using Djohnnie.SoftwareFactory.Application.Features.TodoTasks;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using MediatR;
using Microsoft.Extensions.Logging;
using AgentInfrastructure = Djohnnie.SoftwareFactory.Agents;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

/// <summary>
/// The chat page: the conversations of a repository with the DGHSF Agent, which reads everything DGHSF stores about the
/// repository, its files and (when connected) GitHub through the MCP server. A new conversation is named by the
/// Chat Title Writer background agent (<see cref="AgentInfrastructure.BackgroundAgents.WriteChatTitleAsync"/>).
/// </summary>
public sealed class ProjectChatService(
    AgentInfrastructure.AssistantAgentFactory factory,
    AgentInfrastructure.BackgroundAgents backgroundAgents,
    AgentInfrastructure.IAgentUsageRecorder usageRecorder,
    ISender mediator,
    WorkItemService workItems,
    MindmapService mindmaps,
    KnowledgeBaseService knowledgeBase,
    WorkItemTemplateService templates,
    SkillService skills,
    TodoTaskService todoTasks,
    SyncLogService syncLog,
    GitHubService gitHub,
    ILogger<ProjectChatService> logger)
{
    /// <summary>The longest title taken from the first message until the title writer has named the conversation.</summary>
    public const int FallbackTitleLength = 40;

    public bool IsConfigured => factory.IsConfigured;

    /// <summary>The DGHSF Agent with its current system prompt, model and enabled state.</summary>
    public async Task<InternalAgent> GetAgentAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetInternalAgentQuery(new(InternalAgentKeys.DghsfAgent)), cancellationToken)).Dto;
    }

    /// <summary>The repository's conversations, most recently used first.</summary>
    public async Task<IReadOnlyList<ProjectChatSummary>> GetAllAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetProjectChatsQuery(new(repositoryFullName)), cancellationToken)).Dto.Items;
    }

    /// <summary>The conversation with what the chat shows of it, or <c>null</c> when the repository has no such conversation.</summary>
    public async Task<ProjectChat?> GetAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetProjectChatQuery(new(repositoryFullName, id)), cancellationToken)).Dto;
    }

    /// <summary>Starts a conversation, titled after the first message until <see cref="WriteTitleAsync"/> names it.</summary>
    public async Task<ProjectChatSummary> StartAsync(string repositoryFullName, string firstMessage, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new StartProjectChatCommand(new(repositoryFullName, FallbackTitle(firstMessage))), cancellationToken)).Dto;
    }

    public async Task<ProjectChatSummary> RenameAsync(string repositoryFullName, Guid id, string title, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new RenameProjectChatCommand(new(repositoryFullName, id, title)), cancellationToken)).Dto;
    }

    public async Task DeleteAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new DeleteProjectChatCommand(new(repositoryFullName, id)), cancellationToken);
    }

    /// <summary>
    /// Lets the Chat Title Writer name the conversation after its first message, in two words, and stores the title.
    /// <c>null</c> when it can't (not configured, disabled, nothing usable or a failure); the conversation then keeps its title.
    /// </summary>
    public async Task<ProjectChatSummary?> WriteTitleAsync(string repositoryFullName, Guid id, string firstMessage,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            var titleWriter = (await mediator.Send(new GetInternalAgentQuery(new(InternalAgentKeys.ChatTitleWriter)), cancellationToken)).Dto;
            if (!titleWriter.IsEnabled)
            {
                return null;
            }

            var title = await backgroundAgents.WriteChatTitleAsync(titleWriter, firstMessage, cancellationToken);
            return title is null ? null : await RenameAsync(repositoryFullName, id, title, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The Chat Title Writer could not name conversation {Id} of {Repository}.", id, repositoryFullName);
            return null;
        }
    }

    /// <summary>The DGHSF Agent's tools for the repository.</summary>
    /// <param name="gitHubTools">The GitHub MCP tools of the repository; <c>null</c> when not connected.</param>
    public AssistantTools CreateTools(string repositoryFullName, RepositoryGitHubTools? gitHubTools = null)
    {
        var data = new ProjectAgentData(repositoryFullName, workItems, mindmaps, knowledgeBase, templates, skills, todoTasks, syncLog, gitHub);
        return new(new AgentInfrastructure.ProjectAgentTools(data, gitHubTools?.Session.Tools).AsTools());
    }

    /// <summary>A conversation that continues the stored conversation <paramref name="chatId"/> and adds to it.</summary>
    /// <param name="agent">The DGHSF Agent, as returned by <see cref="GetAgentAsync"/>.</param>
    /// <param name="tools">The tools from <see cref="CreateTools"/>.</param>
    public ProjectConversation CreateConversation(InternalAgent agent, string repositoryFullName, Guid chatId, AssistantTools tools)
    {
        var instructions = $"{agent.Instructions.TrimEnd()}\n\n{AgentInfrastructure.ProjectAgentTools.LinkInstructions}";
        var conversation = factory.CreateConversation(agent.Name, instructions, tools.Tools, agent.Model,
            usage => usageRecorder.RecordAsync(agent, usage, InternalAgentUsageSources.ForCopilot(agent.Key)));
        return new(conversation, repositoryFullName, chatId, mediator);
    }

    /// <summary>The first words of the message, cut off at <see cref="FallbackTitleLength"/> characters.</summary>
    public static string FallbackTitle(string? message)
    {
        var text = string.Join(' ', (message ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0)
        {
            return ProjectChat.DefaultTitle;
        }
        if (text.Length <= FallbackTitleLength)
        {
            return text;
        }

        var cut = text[..FallbackTitleLength];
        var space = cut.LastIndexOf(' ');
        return (space > FallbackTitleLength / 2 ? cut[..space] : cut).TrimEnd() + "…";
    }
}

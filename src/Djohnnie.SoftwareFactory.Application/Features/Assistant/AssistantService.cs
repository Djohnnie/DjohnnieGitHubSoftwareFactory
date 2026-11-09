using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;
using Microsoft.Extensions.Logging;
using AgentInfrastructure = Djohnnie.SoftwareFactory.Agents;

namespace Djohnnie.SoftwareFactory.Application.Features.Assistant;

/// <summary>Runs the internal Copilots of the pages and the Chat Suggestions agent that writes their suggestion chips.</summary>
public sealed class AssistantService(
    AgentInfrastructure.AssistantAgentFactory factory,
    AgentInfrastructure.BackgroundAgents backgroundAgents,
    ISender mediator,
    ILogger<AssistantService> logger)
{
    public const string AgentName = AgentInfrastructure.AssistantAgentFactory.AgentName;
    public bool IsConfigured => factory.IsConfigured;

    /// <summary>The configured model, used by agents without a model of their own.</summary>
    public string? DefaultModel => factory.DefaultModel;

    /// <summary>The Copilot with its current system prompt, model and enabled state.</summary>
    public async Task<InternalAgent> GetCopilotAsync(string key, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetInternalAgentQuery(new(key)), cancellationToken)).Dto;
    }

    public AssistantConversation CreateConversation(InternalAgent copilot, AssistantTools? tools = null)
    {
        return new(factory.CreateConversation(copilot.Name, copilot.Instructions, tools?.Tools, copilot.Model));
    }

    /// <summary>
    /// Lets the Chat Suggestions agent write example prompts for the Copilot, based on the current form.
    /// Empty when the agent is disabled, not configured or fails.
    /// </summary>
    /// <param name="context">Extra page context, e.g. the selected repository.</param>
    public async Task<IReadOnlyList<string>> SuggestPromptsAsync(InternalAgent copilot, string? context, AssistantTools? tools,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        try
        {
            var agent = await GetCopilotAsync(InternalAgentKeys.ChatSuggestions, cancellationToken);
            return agent.IsEnabled
                ? await backgroundAgents.SuggestPromptsAsync(agent, copilot, context, tools?.Tools, cancellationToken)
                : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Chat Suggestions could not write suggestions for {Copilot}.", copilot.Key);
            return [];
        }
    }
}

public sealed class AssistantConversation
{
    private readonly AgentInfrastructure.AssistantConversation _conversation;

    internal AssistantConversation(AgentInfrastructure.AssistantConversation conversation)
    {
        _conversation = conversation;
    }

    public IAsyncEnumerable<string> RunStreamingAsync(string text, CancellationToken cancellationToken = default)
    {
        return _conversation.RunStreamingAsync(text, cancellationToken);
    }

    public void Reset()
    {
        _conversation.Reset();
    }
}

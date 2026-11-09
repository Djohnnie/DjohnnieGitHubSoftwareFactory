using System.Runtime.CompilerServices;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
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

    /// <param name="copilot">The Copilot to talk to.</param>
    /// <param name="tools">The tools of the page.</param>
    /// <param name="owner">What the conversation is stored with; <c>null</c> until that exists, see <see cref="AssistantConversation.AttachAsync"/>.</param>
    public AssistantConversation CreateConversation(InternalAgent copilot, AssistantTools? tools = null, ChatOwner? owner = null)
    {
        return new(factory.CreateConversation(copilot.Name, copilot.Instructions, tools?.Tools, copilot.Model), copilot.Key, owner, mediator);
    }

    /// <summary>What the chat shows of the owner's open conversation; empty when there is none.</summary>
    public async Task<IReadOnlyList<ChatDisplayMessage>> GetHistoryAsync(ChatOwner owner, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetChatDisplayMessagesQuery(new(owner)), cancellationToken)).Dto.Items;
    }

    /// <summary>Closes the owner's open conversation, so the next message starts a new one. The closed one stays stored.</summary>
    public async Task CloseConversationAsync(ChatOwner owner, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new CloseChatConversationCommand(new(owner)), cancellationToken);
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

/// <summary>A conversation with a Copilot that continues the owner's stored conversation and adds to it.</summary>
public sealed class AssistantConversation
{
    private readonly AgentInfrastructure.AssistantConversation _conversation;
    private readonly string _copilotKey;
    private readonly ISender _mediator;
    private readonly List<StoredChatMessage> _unsaved = [];
    private ChatOwner? _owner;
    private bool _restored;

    internal AssistantConversation(AgentInfrastructure.AssistantConversation conversation, string copilotKey, ChatOwner? owner, ISender mediator)
    {
        _conversation = conversation;
        _copilotKey = copilotKey;
        _owner = owner;
        _mediator = mediator;
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(string text, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_restored)
        {
            if (_owner is not null)
            {
                var payloads = await _mediator.Send(new GetChatPayloadsQuery(new(_owner)), cancellationToken);
                await _conversation.RestoreAsync(payloads.Dto.Items, cancellationToken);
            }
            _restored = true;
        }

        await foreach (var update in _conversation.RunStreamingAsync(text, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Stores the finished exchange. It stays in memory until it can be written: while there's no owner yet, or when
    /// writing failed, in which case it is tried again with the next exchange.
    /// </summary>
    public async Task SaveTurnAsync(string text, string reply, bool isError, CancellationToken cancellationToken = default)
    {
        var messages = _conversation.TakeNewMessages(text, reply);
        if (messages.Count > 0)
        {
            _unsaved.AddRange(messages);
        }
        else
        {
            // Stopped or failed: it stays visible in the chat, but isn't part of what the model gets to see.
            _unsaved.Add(new StoredChatMessage(ChatMessageRole.User, text, null));
            _unsaved.Add(new StoredChatMessage(ChatMessageRole.Assistant, reply, null, isError));
        }

        await FlushAsync(cancellationToken);
    }

    /// <summary>Links a conversation that started before its owner existed, and stores what was said so far.</summary>
    public async Task AttachAsync(ChatOwner owner, CancellationToken cancellationToken = default)
    {
        _owner = owner;
        await FlushAsync(cancellationToken);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_owner is null || _unsaved.Count == 0)
        {
            return;
        }

        await _mediator.Send(new AppendChatMessagesCommand(new(_owner, _copilotKey, _unsaved.ToList())), cancellationToken);
        _unsaved.Clear();
    }
}

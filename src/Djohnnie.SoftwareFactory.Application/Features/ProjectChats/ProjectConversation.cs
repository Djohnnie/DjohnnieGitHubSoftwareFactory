using System.Runtime.CompilerServices;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using MediatR;
using AgentInfrastructure = Djohnnie.SoftwareFactory.Agents;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

/// <summary>
/// A conversation with the DGHSF Agent that continues a stored conversation and adds every finished exchange to it. The
/// model gets the last summary and the messages after it; after <see cref="SummarizeIfDueAsync"/> stored a new summary,
/// the next message starts from that.
/// </summary>
public sealed class ProjectConversation
{
    private readonly AgentInfrastructure.AssistantConversation _conversation;
    private readonly string _repositoryFullName;
    private readonly ISender _mediator;
    private readonly List<StoredChatMessage> _unsaved = [];
    // Writing a summary and adding an exchange both add a message at the end; one at a time.
    private readonly SemaphoreSlim _writes = new(1, 1);
    private volatile bool _restored;
    private int _summarizing;

    internal ProjectConversation(AgentInfrastructure.AssistantConversation conversation, string repositoryFullName, Guid chatId, ISender mediator)
    {
        _conversation = conversation;
        _repositoryFullName = repositoryFullName;
        ChatId = chatId;
        _mediator = mediator;
    }

    public Guid ChatId { get; }

    public async IAsyncEnumerable<string> RunStreamingAsync(string text, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Not while an exchange waits to be stored: the history in memory is the only place it is in.
        if (!_restored && _unsaved.Count == 0)
        {
            var payloads = await _mediator.Send(new GetProjectChatPayloadsQuery(new(_repositoryFullName, ChatId)), cancellationToken);
            await _conversation.RestoreAsync(payloads.Dto.Items, cancellationToken);
            _restored = true;
        }

        await foreach (var update in _conversation.RunStreamingAsync(text, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Stores the finished exchange and returns the conversation as it is now. When writing fails, the exchange stays in
    /// memory and is tried again with the next one.
    /// </summary>
    public async Task<ProjectChatSummary> SaveTurnAsync(string text, string reply, bool isError, CancellationToken cancellationToken = default)
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

        await _writes.WaitAsync(cancellationToken);
        try
        {
            var summary = (await _mediator.Send(new AppendProjectChatMessagesCommand(new(_repositoryFullName, ChatId, _unsaved.ToList())), cancellationToken)).Dto;
            _unsaved.Clear();
            return summary;
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <summary>
    /// Lets the Chat Summarizer summarize the conversation when <see cref="ProjectChat.SummaryInterval"/> chat messages
    /// aren't summarized yet; <c>true</c> when it stored a new summary, which the next message then starts from. Does nothing
    /// while it is already summarizing.
    /// </summary>
    public async Task<bool> SummarizeIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _summarizing, 1) == 1)
        {
            return false;
        }

        try
        {
            await _writes.WaitAsync(cancellationToken);
            try
            {
                var summarized = (await _mediator.Send(new SummarizeProjectChatCommand(new(_repositoryFullName, ChatId)), cancellationToken)).Dto.Summarized;
                if (summarized)
                {
                    _restored = false;
                }
                return summarized;
            }
            finally
            {
                _writes.Release();
            }
        }
        finally
        {
            Volatile.Write(ref _summarizing, 0);
        }
    }
}

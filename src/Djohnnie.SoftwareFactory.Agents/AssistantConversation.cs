using System.Runtime.CompilerServices;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

public sealed class AssistantConversation
{
    private static readonly JsonSerializerOptions FormJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly AIAgent _agent;
    private readonly Func<Task<string?>>? _requestContext;
    private const string UserMessageMarker = "\n\nThe user's message:\n";

    private readonly Func<Task<object?>>? _snapshot;
    private readonly WebFetchTool? _webFetch;
    private AgentSession? _session;
    private int _storedMessages;
    private bool _resumed;

    internal AssistantConversation(AIAgent agent, Func<Task<string?>>? requestContext = null, Func<Task<object?>>? snapshot = null, WebFetchTool? webFetch = null)
    {
        _agent = agent;
        _requestContext = requestContext;
        _snapshot = snapshot;
        _webFetch = webFetch;
    }

    /// <summary>Continues a stored conversation.</summary>
    /// <param name="payloads">The messages as <see cref="TakeNewMessages"/> serialized them, in order.</param>
    /// <param name="cancellationToken">Cancels loading the session.</param>
    public async Task RestoreAsync(IReadOnlyList<string> payloads, CancellationToken cancellationToken = default)
    {
        if (payloads.Count == 0)
        {
            return;
        }

        _session = await _agent.CreateSessionAsync(cancellationToken);
        var history = payloads.Select(payload => JsonSerializer.Deserialize<ChatMessage>(payload, AIJsonUtilities.DefaultOptions)!).ToList();
        _session.SetInMemoryChatHistory(history);
        foreach (var message in history.Where(m => m.Role == ChatRole.User))
        {
            _webFetch?.AllowUrlsIn(WithoutRequestContext(message.Text));
        }
        _storedMessages = payloads.Count;
        _resumed = true;
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(string text, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _session ??= await _agent.CreateSessionAsync(cancellationToken);
        // Only what the user typed, not the form in the request context, which can hold text that came from elsewhere.
        _webFetch?.AllowUrlsIn(text);
        var context = await BuildContextAsync();
        var message = string.IsNullOrEmpty(context) ? text : $"{context}{UserMessageMarker}{text}";
        await foreach (var update in _agent.RunStreamingAsync(message, _session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    /// <summary>
    /// The messages the last turn added to the history, ready to store; empty when it didn't complete, because a
    /// stopped or failed turn isn't part of the history.
    /// </summary>
    /// <param name="userText">The user's message as the chat shows it, without the request context.</param>
    /// <param name="replyText">The reply as the chat shows it.</param>
    public IReadOnlyList<StoredChatMessage> TakeNewMessages(string userText, string replyText)
    {
        if (_session is null || !_session.TryGetInMemoryChatHistory(out var history) || history.Count <= _storedMessages)
        {
            return [];
        }

        var added = history.Skip(_storedMessages).Select(WithoutReasoning).OfType<ChatMessage>().ToList();
        _storedMessages = history.Count;

        var firstUser = added.FindIndex(m => m.Role == ChatRole.User);
        var lastAssistant = added.FindLastIndex(m => m.Role == ChatRole.Assistant);
        var messages = added.Select((message, index) => new StoredChatMessage(
            ToRole(message),
            index == firstUser ? userText : index == lastAssistant ? replyText : null,
            JsonSerializer.Serialize(message, AIJsonUtilities.DefaultOptions))).ToList();
        if (lastAssistant < 0)
        {
            messages.Add(new StoredChatMessage(ChatMessageRole.Assistant, replyText, null));
        }

        return messages;
    }

    /// <summary>A reopened conversation still has what the tools read back then, so the first message says what the form is now.</summary>
    private async Task<string?> BuildContextAsync()
    {
        var context = _requestContext is null ? null : await _requestContext();
        if (!_resumed)
        {
            return context;
        }

        _resumed = false;
        if (context is not null || _snapshot is null || await _snapshot() is not { } form)
        {
            return context;
        }

        return $"""
            This conversation was reopened. The form may have changed since the messages above; it is now:
            {JsonSerializer.Serialize(form, form.GetType(), FormJsonOptions)}
            """;
    }

    /// <summary>What the user typed in a stored message, without the request context that was sent along.</summary>
    private static string WithoutRequestContext(string text)
    {
        var index = text.LastIndexOf(UserMessageMarker, StringComparison.Ordinal);
        return index < 0 ? text : text[(index + UserMessageMarker.Length)..];
    }

    /// <summary>Reasoning only means something to the service that produced it, which can't be relied on later.</summary>
    private static ChatMessage? WithoutReasoning(ChatMessage message)
    {
        if (!message.Contents.Any(content => content is TextReasoningContent))
        {
            return message;
        }

        var contents = message.Contents.Where(content => content is not TextReasoningContent).ToList();
        if (contents.Count == 0)
        {
            return null;
        }

        var copy = message.Clone();
        copy.Contents = contents;
        return copy;
    }

    private static ChatMessageRole ToRole(ChatMessage message)
    {
        if (message.Role == ChatRole.User)
        {
            return ChatMessageRole.User;
        }

        return message.Role == ChatRole.Tool ? ChatMessageRole.Tool : ChatMessageRole.Assistant;
    }
}

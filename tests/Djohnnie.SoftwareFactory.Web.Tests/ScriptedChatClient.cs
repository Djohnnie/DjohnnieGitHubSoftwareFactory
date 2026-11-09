using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>A chat client that replays scripted responses, and records what the agent sent.</summary>
internal sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new(responses);

    public List<List<ChatMessage>> Requests { get; } = [];

    public List<ChatOptions?> Options { get; } = [];

    /// <summary>Adds a response to the script; once the script runs out, a request fails.</summary>
    public void Enqueue(ChatResponse response)
    {
        _responses.Enqueue(response);
    }

    /// <summary>A response in which the model calls a tool.</summary>
    public static ChatResponse ToolCall(string name, IDictionary<string, object?> arguments)
    {
        return new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), name, arguments)]));
    }

    public static ChatResponse Text(string text)
    {
        return new(new ChatMessage(ChatRole.Assistant, text));
    }

    /// <summary>Adds the tokens the service reports for a response.</summary>
    public static ChatResponse WithUsage(ChatResponse response, long inputTokens, long outputTokens, long cachedInputTokens = 0, long reasoningTokens = 0)
    {
        response.Usage = new UsageDetails
        {
            InputTokenCount = inputTokens,
            OutputTokenCount = outputTokens,
            CachedInputTokenCount = cachedInputTokens,
            ReasoningTokenCount = reasoningTokens
        };
        return response;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
        Options.Add(options);
        return Task.FromResult(_responses.Dequeue());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}

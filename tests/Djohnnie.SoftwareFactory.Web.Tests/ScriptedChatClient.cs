using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>A chat client that replays scripted responses, and records what the agent sent.</summary>
internal sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new(responses);

    public List<List<ChatMessage>> Requests { get; } = [];

    /// <summary>A response in which the model calls a tool.</summary>
    public static ChatResponse ToolCall(string name, IDictionary<string, object?> arguments)
    {
        return new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), name, arguments)]));
    }

    public static ChatResponse Text(string text)
    {
        return new(new ChatMessage(ChatRole.Assistant, text));
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
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

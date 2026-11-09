using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;

namespace Djohnnie.SoftwareFactory.Agents;

public sealed class AssistantConversation
{
    private readonly AIAgent _agent;
    private readonly Func<Task<string?>>? _requestContext;
    private AgentSession? _session;

    internal AssistantConversation(AIAgent agent, Func<Task<string?>>? requestContext = null)
    {
        _agent = agent;
        _requestContext = requestContext;
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(string text, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _session ??= await _agent.CreateSessionAsync(cancellationToken);
        var context = _requestContext is null ? null : await _requestContext();
        var message = string.IsNullOrEmpty(context) ? text : $"{context}\n\nThe user's message:\n{text}";
        await foreach (var update in _agent.RunStreamingAsync(message, _session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    public void Reset()
    {
        _session = null;
    }
}

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;

namespace Djohnnie.SoftwareFactory.Agents;

public sealed class AssistantConversation
{
    private readonly AIAgent _agent;
    private AgentSession? _session;

    internal AssistantConversation(AIAgent agent) => _agent = agent;

    public async IAsyncEnumerable<string> RunStreamingAsync(string text, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _session ??= await _agent.CreateSessionAsync(cancellationToken);
        await foreach (var update in _agent.RunStreamingAsync(text, _session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    public void Reset() => _session = null;
}

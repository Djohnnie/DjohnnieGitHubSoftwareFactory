using AgentInfrastructure = Djohnnie.SoftwareFactory.Agents;

namespace Djohnnie.SoftwareFactory.Application.Features.Assistant;

public sealed class AssistantService(AgentInfrastructure.AssistantAgentFactory factory)
{
    public const string AgentName = AgentInfrastructure.AssistantAgentFactory.AgentName;
    public bool IsConfigured => factory.IsConfigured;

    public AssistantConversation CreateConversation(string instructions, AssistantTools? tools = null)
    {
        return new(factory.CreateConversation(instructions, tools?.Tools));
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

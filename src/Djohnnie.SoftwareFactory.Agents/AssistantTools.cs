using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

public sealed record AssistantTools
{
    internal IList<AITool> Tools { get; }

    internal AssistantTools(IList<AITool> tools)
    {
        Tools = tools;
    }
}

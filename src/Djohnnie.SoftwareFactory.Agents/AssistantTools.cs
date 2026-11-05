using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

public sealed record AssistantTools
{
    internal IList<AITool> Tools { get; }

    /// <summary>Reads the current form, e.g. for the Chat Suggestions agent; <c>null</c> when there is no form.</summary>
    internal Func<Task<object?>>? Snapshot { get; }

    internal AssistantTools(IList<AITool> tools, Func<Task<object?>>? snapshot = null)
    {
        Tools = tools;
        Snapshot = snapshot;
    }
}

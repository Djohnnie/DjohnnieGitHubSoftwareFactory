using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

public sealed record AssistantTools
{
    internal IList<AITool> Tools { get; }

    /// <summary>Reads the current form, e.g. for the Chat Suggestions agent; <c>null</c> when there is no form.</summary>
    internal Func<Task<object?>>? Snapshot { get; }

    /// <summary>Builds the context that is sent along with every user message; <c>null</c> when there is none.</summary>
    internal Func<Task<string?>>? RequestContext { get; }

    internal AssistantTools(IList<AITool> tools, Func<Task<object?>>? snapshot = null, Func<Task<string?>>? requestContext = null)
    {
        Tools = tools;
        Snapshot = snapshot;
        RequestContext = requestContext;
    }
}

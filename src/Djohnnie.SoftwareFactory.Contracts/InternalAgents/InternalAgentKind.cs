namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

public enum InternalAgentKind
{
    /// <summary>A chat agent on a page that helps the user fill in a form. Stored by name, so it keeps this historical name.</summary>
    Copilot,

    /// <summary>An agent that runs without a chat, e.g. to pick a model or to write suggestions.</summary>
    Background
}

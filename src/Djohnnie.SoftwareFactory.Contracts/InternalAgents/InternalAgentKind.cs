namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

public enum InternalAgentKind
{
    /// <summary>A chat assistant on a page that helps the user fill in a form.</summary>
    Copilot,

    /// <summary>An agent that runs without a chat, e.g. to pick a model or to write suggestions.</summary>
    Background
}

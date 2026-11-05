using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>The fixed keys of DGHSF's internal agents.</summary>
public static class InternalAgentKeys
{
    public const string AgentCopilot = "agent-copilot";
    public const string AgentTemplateCopilot = "agent-template-copilot";
    public const string CopilotInstructionsCopilot = "copilot-instructions-copilot";
    public const string ModelSelector = "model-selector";
    public const string ChatSuggestions = "chat-suggestions";

    /// <summary>The Copilot of the work item pages of a type, e.g. <c>story-copilot</c>.</summary>
    public static string ForWorkItem(WorkItemType type)
    {
        return $"{type.Label}-copilot";
    }

    /// <summary>The Copilot of the template pages of a work item type, e.g. <c>story-template-copilot</c>.</summary>
    public static string ForWorkItemTemplate(WorkItemType type)
    {
        return $"{type.Label}-template-copilot";
    }
}

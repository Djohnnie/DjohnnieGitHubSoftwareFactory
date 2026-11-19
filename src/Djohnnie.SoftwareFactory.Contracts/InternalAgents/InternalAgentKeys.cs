using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>
/// The fixed keys of DGHSF's internal agents. They are stored with the agents, their usage and their conversations, so
/// they keep their original names when an agent is renamed (the chat agents used to be called Copilots).
/// </summary>
public static class InternalAgentKeys
{
    public const string AgentCopilot = "agent-copilot";
    public const string AgentTemplateCopilot = "agent-template-copilot";
    public const string CopilotInstructionsCopilot = "copilot-instructions-copilot";
    public const string CopilotSkillsAgent = "copilot-skills-agent";
    public const string ModelSelector = "model-selector";
    public const string ChatSuggestions = "chat-suggestions";
    public const string DescriptionWriter = "description-writer";
    public const string WorkItemBreakdown = "work-item-breakdown";
    public const string MindmapWorkItemWriter = "mindmap-work-item-writer";
    public const string MindmapPlanner = "mindmap-planner";
    public const string RepositoryAnalyst = "repository-analyst";
    public const string RepositoryWorkItemWriter = "repository-work-item-writer";
    public const string ChatTitleWriter = "chat-title-writer";
    public const string ChatSummarizer = "chat-summarizer";

    /// <summary>The chat agent of the chat page, which can read everything DGHSF and GitHub know about the repository.</summary>
    public const string DghsfAgent = "dghsf-agent";

    /// <summary>The chat agent of the work item pages of a type, e.g. <c>story-copilot</c>.</summary>
    public static string ForWorkItem(WorkItemType type)
    {
        return $"{type.Label}-copilot";
    }

    /// <summary>The chat agent of the template pages of a work item type, e.g. <c>story-template-copilot</c>.</summary>
    public static string ForWorkItemTemplate(WorkItemType type)
    {
        return $"{type.Label}-template-copilot";
    }
}

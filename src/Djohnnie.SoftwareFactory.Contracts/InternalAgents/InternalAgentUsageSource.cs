using System.ComponentModel;

namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>What caused a model request of an internal agent. Stored by name, so members can be added but not renamed.</summary>
public enum InternalAgentUsageSource
{
    /// <summary>A request recorded before sources were tracked.</summary>
    [Description("Unknown")] Unknown = 0,
    [Description("Model test")] ModelTest,
    [Description("Epic Agent Chat")] EpicCopilotChat,
    [Description("Feature Agent Chat")] FeatureCopilotChat,
    [Description("User Story Agent Chat")] StoryCopilotChat,
    [Description("Bug Agent Chat")] BugCopilotChat,
    [Description("Task Agent Chat")] TaskCopilotChat,
    [Description("Epic Template Agent Chat")] EpicTemplateCopilotChat,
    [Description("Feature Template Agent Chat")] FeatureTemplateCopilotChat,
    [Description("User Story Template Agent Chat")] StoryTemplateCopilotChat,
    [Description("Bug Template Agent Chat")] BugTemplateCopilotChat,
    [Description("Task Template Agent Chat")] TaskTemplateCopilotChat,
    [Description("Custom Agent Agent Chat")] AgentCopilotChat,
    [Description("Custom Agent Template Agent Chat")] AgentTemplateCopilotChat,
    [Description("Copilot Instructions Agent Chat")] CopilotInstructionsCopilotChat,
    [Description("Model Selector")] ModelSelector,
    [Description("Chat Suggestions")] ChatSuggestions,
    [Description("Description Writer")] DescriptionWriter,
    [Description("Work Item Breakdown")] WorkItemBreakdown,
    [Description("Mindmap Work Item Writer")] MindmapWorkItemWriter,
    [Description("Copilot Skills Agent Chat")] CopilotSkillsAgentChat
}

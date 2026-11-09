using System.ComponentModel;

namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>What caused a model request of an internal agent. Stored by name, so members can be added but not renamed.</summary>
public enum InternalAgentUsageSource
{
    /// <summary>A request recorded before sources were tracked.</summary>
    [Description("Unknown")] Unknown = 0,
    [Description("Model test")] ModelTest,
    [Description("Epic Copilot Chat")] EpicCopilotChat,
    [Description("Feature Copilot Chat")] FeatureCopilotChat,
    [Description("User Story Copilot Chat")] StoryCopilotChat,
    [Description("Bug Copilot Chat")] BugCopilotChat,
    [Description("Task Copilot Chat")] TaskCopilotChat,
    [Description("Epic Template Copilot Chat")] EpicTemplateCopilotChat,
    [Description("Feature Template Copilot Chat")] FeatureTemplateCopilotChat,
    [Description("User Story Template Copilot Chat")] StoryTemplateCopilotChat,
    [Description("Bug Template Copilot Chat")] BugTemplateCopilotChat,
    [Description("Task Template Copilot Chat")] TaskTemplateCopilotChat,
    [Description("Agent Copilot Chat")] AgentCopilotChat,
    [Description("Agent Template Copilot Chat")] AgentTemplateCopilotChat,
    [Description("Copilot Instructions Copilot Chat")] CopilotInstructionsCopilotChat,
    [Description("Model Selector")] ModelSelector,
    [Description("Chat Suggestions")] ChatSuggestions,
    [Description("Description Writer")] DescriptionWriter,
    [Description("Work Item Breakdown")] WorkItemBreakdown
}

using System.ComponentModel;
using System.Reflection;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

public static class InternalAgentUsageSources
{
    private static readonly Dictionary<string, InternalAgentUsageSource> CopilotChats = new(StringComparer.OrdinalIgnoreCase)
    {
        [InternalAgentKeys.ForWorkItem(WorkItemType.Epic)] = InternalAgentUsageSource.EpicCopilotChat,
        [InternalAgentKeys.ForWorkItem(WorkItemType.Feature)] = InternalAgentUsageSource.FeatureCopilotChat,
        [InternalAgentKeys.ForWorkItem(WorkItemType.Story)] = InternalAgentUsageSource.StoryCopilotChat,
        [InternalAgentKeys.ForWorkItem(WorkItemType.Bug)] = InternalAgentUsageSource.BugCopilotChat,
        [InternalAgentKeys.ForWorkItem(WorkItemType.Task)] = InternalAgentUsageSource.TaskCopilotChat,
        [InternalAgentKeys.ForWorkItemTemplate(WorkItemType.Epic)] = InternalAgentUsageSource.EpicTemplateCopilotChat,
        [InternalAgentKeys.ForWorkItemTemplate(WorkItemType.Feature)] = InternalAgentUsageSource.FeatureTemplateCopilotChat,
        [InternalAgentKeys.ForWorkItemTemplate(WorkItemType.Story)] = InternalAgentUsageSource.StoryTemplateCopilotChat,
        [InternalAgentKeys.ForWorkItemTemplate(WorkItemType.Bug)] = InternalAgentUsageSource.BugTemplateCopilotChat,
        [InternalAgentKeys.ForWorkItemTemplate(WorkItemType.Task)] = InternalAgentUsageSource.TaskTemplateCopilotChat,
        [InternalAgentKeys.AgentCopilot] = InternalAgentUsageSource.AgentCopilotChat,
        [InternalAgentKeys.AgentTemplateCopilot] = InternalAgentUsageSource.AgentTemplateCopilotChat,
        [InternalAgentKeys.CopilotInstructionsCopilot] = InternalAgentUsageSource.CopilotInstructionsCopilotChat
    };

    /// <summary>The source of a chat with the Copilot with this key.</summary>
    public static InternalAgentUsageSource ForCopilot(string key)
    {
        return CopilotChats.GetValueOrDefault(key, InternalAgentUsageSource.Unknown);
    }

    extension(InternalAgentUsageSource source)
    {
        /// <summary>The text to show for the source.</summary>
        public string Description()
        {
            var field = typeof(InternalAgentUsageSource).GetField(source.ToString());
            return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? source.ToString();
        }
    }
}

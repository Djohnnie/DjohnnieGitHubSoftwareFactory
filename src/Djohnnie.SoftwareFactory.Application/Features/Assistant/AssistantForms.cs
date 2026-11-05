using AgentInfrastructure = Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Application.Features.Assistant;

public sealed record AssistantTools
{
    internal AgentInfrastructure.AssistantTools Tools { get; }
    internal AssistantTools(AgentInfrastructure.AssistantTools tools)
    {
        Tools = tools;
    }
}

public static class AssistantForms
{
    public const string AgentInstructions = AgentInfrastructure.AgentFormTools.Instructions;
    public const string TemplateInstructions = AgentInfrastructure.AgentFormTools.TemplateInstructions;

    public static string WorkItemInstructions(WorkItemType type)
    {
        return AgentInfrastructure.WorkItemFormTools.InstructionsFor(type);
    }

    public static AssistantTools ForAgent(
        Func<AgentDefinition> getAgent, Func<string?> getFileName, Action<string>? setFileName,
        IReadOnlyList<AiModel> models, Func<Action, Task> onUi)
    {
        return new(new AgentInfrastructure.AgentFormTools(getAgent, getFileName, setFileName, models, onUi).AsTools());
    }

    /// <summary>Tools for the work item form; the assignment tools are only included for stories, bugs and tasks.</summary>
    public static AssistantTools ForWorkItem(
        Func<WorkItem> getItem, IReadOnlyList<AiModel> models,
        Func<CancellationToken, Task<List<AgentSummary>>> loadAgents, Func<Action, Task> onUi)
    {
        return new(new AgentInfrastructure.WorkItemFormTools(getItem, models, loadAgents, onUi).AsTools());
    }
}

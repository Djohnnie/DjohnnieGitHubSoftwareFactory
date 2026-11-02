using AgentInfrastructure = Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Epics;

namespace Djohnnie.SoftwareFactory.Application.Features.Assistant;

public sealed record AssistantTools
{
    internal AgentInfrastructure.AssistantTools Tools { get; }
    internal AssistantTools(AgentInfrastructure.AssistantTools tools) => Tools = tools;
}

public static class AssistantForms
{
    public const string AgentInstructions = AgentInfrastructure.AgentFormTools.Instructions;
    public const string TemplateInstructions = AgentInfrastructure.AgentFormTools.TemplateInstructions;
    public const string EpicInstructions = AgentInfrastructure.EpicFormTools.Instructions;

    public static AssistantTools ForAgent(
        Func<AgentDefinition> getAgent, Func<string?> getFileName, Action<string>? setFileName,
        IReadOnlyList<AiModel> models, Func<Action, Task> onUi) =>
        new(new AgentInfrastructure.AgentFormTools(getAgent, getFileName, setFileName, models, onUi).AsTools());

    public static AssistantTools ForEpic(
        Func<Epic> getEpic, IReadOnlyList<AiModel> models,
        Func<CancellationToken, Task<List<AgentSummary>>> loadAgents, Func<Action, Task> onUi) =>
        new(new AgentInfrastructure.EpicFormTools(getEpic, models, loadAgents, onUi).AsTools());
}

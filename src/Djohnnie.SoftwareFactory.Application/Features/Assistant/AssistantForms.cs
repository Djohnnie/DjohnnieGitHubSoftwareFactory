using AgentInfrastructure = Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;

namespace Djohnnie.SoftwareFactory.Application.Features.Assistant;

public sealed record AssistantTools
{
    internal AgentInfrastructure.AssistantTools Tools { get; }
    internal AssistantTools(AgentInfrastructure.AssistantTools tools)
    {
        Tools = tools;
    }
}

/// <summary>The form tools of the chat agents; the system prompts are managed as internal agents.</summary>
public static class AssistantForms
{
    public static AssistantTools ForAgent(
        Func<AgentDefinition> getAgent, Func<string?> getFileName, Action<string>? setFileName,
        IReadOnlyList<AiModel> models, Func<Action, Task> onUi)
    {
        return new(new AgentInfrastructure.AgentFormTools(getAgent, getFileName, setFileName, models, onUi).AsTools());
    }

    /// <summary>Tools for the work item form; the assignment tools are only included for stories, bugs and tasks.</summary>
    /// <param name="loadAncestors">Loads a parent and every item above it, closest first, for their technical details.</param>
    public static AssistantTools ForWorkItem(
        Func<WorkItem> getItem, Func<WorkItemTemplate?> getSelectedTemplate, IReadOnlyList<AiModel> models,
        Func<CancellationToken, Task<List<AgentSummary>>> loadAgents, Func<Action, Task> onUi,
        Func<Guid, CancellationToken, Task<IReadOnlyList<WorkItem>>> loadAncestors)
    {
        return new(new AgentInfrastructure.WorkItemFormTools(getItem, getSelectedTemplate, models, loadAgents, onUi, loadAncestors).AsTools());
    }

    /// <summary>
    /// Tools for the Copilot instructions editor, including read-only access to the repository's files and, when
    /// connected, the GitHub MCP tools pinned to the repository (commits, issues, pull requests, workflow runs, ...).
    /// </summary>
    public static AssistantTools ForCopilotInstructions(
        Func<CopilotInstructionsFormSnapshot> getForm, Action<string> setContent,
        Func<string, CancellationToken, Task<IReadOnlyList<RepositoryEntry>>> listDirectory,
        Func<string, CancellationToken, Task<string?>> readFile, Func<Action, Task> onUi,
        RepositoryGitHubTools? gitHubTools = null)
    {
        return new(new AgentInfrastructure.CopilotInstructionsFormTools(getForm, setContent, listDirectory, readFile, onUi,
            gitHubTools?.Session.Tools).AsTools());
    }

    /// <param name="isNew">Whether the skill hasn't been saved yet; renaming a saved skill is reported to the agent.</param>
    public static AssistantTools ForSkill(Func<Skill> getSkill, bool isNew, Func<Action, Task> onUi)
    {
        return new(new AgentInfrastructure.SkillFormTools(getSkill, isNew, onUi).AsTools());
    }

    public static AssistantTools ForWorkItemTemplate(Func<WorkItemTemplate> getTemplate, Func<Action, Task> onUi)
    {
        return new(new AgentInfrastructure.WorkItemTemplateFormTools(getTemplate, onUi).AsTools());
    }
}

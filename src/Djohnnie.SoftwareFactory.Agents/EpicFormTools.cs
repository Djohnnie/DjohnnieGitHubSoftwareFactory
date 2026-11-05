using System.ComponentModel;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Tools that let DGHSF Copilot read and prefill the epic form. The form is only changed in the UI;
/// the user still saves or accepts the epic.
/// </summary>
/// <param name="getEpic">The epic being edited.</param>
/// <param name="models">The enabled managed models.</param>
/// <param name="loadAgents">Loads the custom agents of the selected repository.</param>
/// <param name="onUi">Runs a change on the UI thread and re-renders the page.</param>
public sealed class EpicFormTools(
    Func<Epic> getEpic,
    IReadOnlyList<AiModel> models,
    Func<CancellationToken, Task<List<AgentSummary>>> loadAgents,
    Func<Action, Task> onUi)
{
    public const string Instructions = """
        ## Your task: plan an epic
        The user is on the epic page of the selected repository and wants your help to write an epic.
        An epic becomes a GitHub issue (labelled `epic`) that is assigned to the Copilot cloud agent, which implements it
        from the issue description. So the description must be clear and complete enough for an autonomous coding agent.

        How to work:
        1. Call `get_epic_form` first to see what is already filled in.
        2. If the request is too vague, ask at most 3 short, numbered follow-up questions at a time
           (for example: the goal and users, what is in and out of scope, constraints, how to verify it is done).
           Don't ask about things you can reasonably decide yourself.
        3. As soon as you know enough, call `update_epic_form` to prefill the form: a short, action-oriented title and
           a Markdown description. Only pass the fields you want to change. Refine the form with later calls when the user
           gives more details.
        4. Assignment settings: call `list_custom_agents` and suggest a fitting custom agent when one clearly matches;
           set a model only when the user asks for one (pick an id from `list_models`); set a base branch or custom
           instructions only when the user mentions them.
        5. After updating, briefly summarize what you filled in and suggest what could still be refined, then remind the
           user to review the form and click "Save draft" or "Accept & assign".

        Structure of a good epic description (Markdown, in English):
        ## Goal  (why, and for whom)
        ## Scope  (what is included, as a bullet list; and what is explicitly out of scope)
        ## Requirements  (functional and technical details, constraints, affected areas)
        ## Acceptance criteria  (a checklist with `- [ ]` items that can be verified)
        """;

    public AssistantTools AsTools()
    {
        return new(
        [
            AIFunctionFactory.Create(GetFormAsync, "get_epic_form"),
            AIFunctionFactory.Create(UpdateFormAsync, "update_epic_form"),
            AIFunctionFactory.Create(ListCustomAgentsAsync, "list_custom_agents"),
            AIFunctionFactory.Create(ListModels, "list_models")
        ]);
    }

    [Description("Gets the current values of the epic form.")]
    public async Task<EpicFormSnapshot> GetFormAsync()
    {
        EpicFormSnapshot? form = null;
        await onUi(() =>
        {
            var epic = getEpic();
            form = new(epic.Status.ToString(), epic.Title, epic.Description,
                epic.CustomAgent ?? "(default Copilot agent)", epic.Model ?? "(auto)",
                epic.BaseBranch ?? "(default branch)", epic.CustomInstructions);
        });
        return form!;
    }

    [Description("Lists the custom agents of the repository that the Copilot cloud agent can use.")]
    public async Task<IReadOnlyList<CustomAgentChoice>> ListCustomAgentsAsync(CancellationToken cancellationToken = default)
    {
        return (await loadAgents(cancellationToken))
            .Where(a => a.AvailableOnGitHub)
            .Select(a => new CustomAgentChoice(a.Id, a.Name, a.Description))
            .ToList();
    }

    [Description("Lists the models the Copilot cloud agent can use for the assignment.")]
    public IReadOnlyList<ModelChoice> ListModels()
    {
        return AgentFormTools.ModelChoices(models);
    }

    [Description("Prefills or changes fields of the epic form. Only pass the fields to change; omit (null) the others.")]
    public async Task<string> UpdateFormAsync(
        [Description("A short, action-oriented title; becomes the issue title.")] string? title = null,
        [Description("The full Markdown description; becomes the issue body. Replaces the current description.")] string? description = null,
        [Description("A custom agent id from list_custom_agents, or 'default' for the default Copilot agent.")] string? customAgent = null,
        [Description("A model id from list_models, or 'auto' to let Copilot pick.")] string? model = null,
        [Description("The branch Copilot starts from, or 'default' for the default branch.")] string? baseBranch = null,
        [Description("Extra guidance for Copilot on top of the issue, or an empty string to clear it.")] string? customInstructions = null,
        CancellationToken cancellationToken = default)
    {
        var changed = new List<string>();
        var problems = new List<string>();

        // Resolve the agent before touching the form; this calls GitHub.
        string? agentId = null;
        var agentValid = false;
        if (customAgent is not null)
        {
            var value = AgentDefinition.GetFileName(customAgent.Trim());
            if (value.Length == 0 || value is "default" or "copilot" or "(default Copilot agent)")
            {
                agentValid = true;
            }
            else
            {
                var agents = await loadAgents(cancellationToken);
                var match = agents.FirstOrDefault(a => string.Equals(a.Id, value, StringComparison.OrdinalIgnoreCase))
                    ?? agents.FirstOrDefault(a => string.Equals(a.Name, customAgent.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is { AvailableOnGitHub: true })
                {
                    agentId = match.Id;
                    agentValid = true;
                }
                else
                {
                    problems.Add(match is null
                        ? $"Unknown custom agent '{customAgent}'; pick an id from list_custom_agents."
                        : $"'{match.Id}' is a VS Code only agent and cannot be used by the cloud agent.");
                }
            }
        }

        await onUi(() =>
        {
            var epic = getEpic();
            var canEditContent = epic.IsDraft;
            var canEditAssignment = epic.Status is WorkItemStatus.Draft or WorkItemStatus.Accepted;

            if (title is not null || description is not null)
            {
                if (!canEditContent)
                {
                    problems.Add("The epic is already accepted: its title and description can no longer be changed.");
                }
                else
                {
                    if (title is not null)
                    {
                        epic.Title = title.Trim();
                        changed.Add("title");
                    }
                    if (description is not null)
                    {
                        epic.Description = description.Replace("\r\n", "\n").Trim();
                        changed.Add("description");
                    }
                }
            }

            var assignmentRequested = customAgent is not null || model is not null || baseBranch is not null || customInstructions is not null;
            if (assignmentRequested && !canEditAssignment)
            {
                problems.Add("The epic is already assigned: its assignment settings can no longer be changed.");
                return;
            }

            if (customAgent is not null && agentValid)
            {
                epic.CustomAgent = agentId;
                changed.Add("custom agent");
            }

            if (model is not null)
            {
                if (AgentFormTools.TryResolveModel(models, model, out var modelId))
                {
                    epic.Model = modelId;
                    changed.Add("model");
                }
                else
                {
                    problems.Add($"Unknown model '{model}'; pick a model id from list_models.");
                }
            }

            if (baseBranch is not null)
            {
                var branch = baseBranch.Trim();
                epic.BaseBranch = branch.Length == 0 || branch is "default" or "(default branch)" ? null : branch;
                changed.Add("base branch");
            }

            if (customInstructions is not null)
            {
                epic.CustomInstructions = string.IsNullOrWhiteSpace(customInstructions) ? null : customInstructions.Trim();
                changed.Add("custom instructions");
            }
        });

        return AgentFormTools.Result(changed, problems);
    }
}

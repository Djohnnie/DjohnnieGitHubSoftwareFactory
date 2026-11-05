using System.ComponentModel;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Tools that let DGHSF Copilot read and prefill a work item form (epic, feature, user story, bug or task).
/// The form is only changed in the UI; the user still saves or accepts the item. The assignment tools are only
/// offered for types that are assigned to the Copilot cloud agent.
/// </summary>
/// <param name="getItem">The work item being edited.</param>
/// <param name="models">The enabled managed models.</param>
/// <param name="loadAgents">Loads the custom agents of the selected repository.</param>
/// <param name="onUi">Runs a change on the UI thread and re-renders the page.</param>
public sealed class WorkItemFormTools(
    Func<WorkItem> getItem,
    IReadOnlyList<AiModel> models,
    Func<CancellationToken, Task<List<AgentSummary>>> loadAgents,
    Func<Action, Task> onUi)
{
    public static string InstructionsFor(WorkItemType type)
    {
        var name = type.DisplayName.ToLowerInvariant();
        var article = type == WorkItemType.Epic ? "an" : "a";
        var purpose = type.CanBeAssigned
            ? $"""
              {Capitalized(article)} {name} becomes a GitHub issue (labelled `{type.Label}`) that is assigned to the Copilot cloud agent, which
              implements it from the issue description. So the description must be clear and complete enough for an autonomous coding agent.
              """
            : $"""
              {Capitalized(article)} {name} becomes a GitHub issue (labelled `{type.Label}`) that structures the work: it is never assigned
              to an agent itself. Its {string.Join(" and ", type.ChildTypes.Select(t => t.PluralName.ToLowerInvariant()))} become sub-issues and are implemented separately,
              so describe the outcome and the boundaries rather than implementation details.
              """;
        var assignment = type.CanBeAssigned
            ? """
              4. Assignment settings: call `list_custom_agents` and suggest a fitting custom agent when one clearly matches;
                 set a model only when the user asks for one (pick an id from `list_models`); set a base branch or custom
                 instructions only when the user mentions them.
              5. After updating, briefly summarize what you filled in and suggest what could still be refined, then remind the
                 user to review the form and click "Save draft" or "Accept & assign".
              """
            : """
              4. After updating, briefly summarize what you filled in and suggest what could still be refined, then remind the
                 user to review the form and click "Save draft" or "Accept & create issue".
              """;

        return $"""
            ## Your task: write {article} {name}
            The user is on the {name} page of the selected repository and wants your help to write {article} {name}.
            {purpose}
            The form shows the parent it belongs to, if any; the user picks the parent, you don't.

            How to work:
            1. Call `get_work_item_form` first to see what is already filled in.
            2. If the request is too vague, ask at most 3 short, numbered follow-up questions at a time
               ({Questions(type)}). Don't ask about things you can reasonably decide yourself.
            3. As soon as you know enough, call `update_work_item_form` to prefill the form: a short, action-oriented title and
               a Markdown description. Only pass the fields you want to change. Refine the form with later calls when the user
               gives more details.
            {assignment}
            Structure of a good {name} description (Markdown, in English):
            {Structure(type)}
            """;
    }

    private static string Capitalized(string value)
    {
        return string.Concat(value[..1].ToUpperInvariant(), value[1..]);
    }

    private static string Questions(WorkItemType type)
    {
        return type switch
        {
            WorkItemType.Bug => "for example: the steps to reproduce, what was expected and what happened instead, where it happens",
            WorkItemType.Story => "for example: who the user is, what they want to achieve and why, how to verify it is done",
            WorkItemType.Task => "for example: what exactly needs to change, constraints, how to verify it is done",
            _ => "for example: the goal and users, what is in and out of scope, constraints, how to verify it is done"
        };
    }

    private static string Structure(WorkItemType type)
    {
        return type switch
        {
            WorkItemType.Epic => """
                ## Goal  (why, and for whom)
                ## Scope  (the capabilities included, as a bullet list; and what is explicitly out of scope)
                ## Success criteria  (how we know the epic delivered its value)
                """,
            WorkItemType.Feature => """
                ## Goal  (what the feature enables, and for whom)
                ## Scope  (what is included, as a bullet list; and what is explicitly out of scope)
                ## Requirements  (functional and non-functional requirements, constraints)
                ## Acceptance criteria  (a checklist with `- [ ]` items that can be verified)
                """,
            WorkItemType.Story => """
                As a <type of user>, I want <goal> so that <reason>.  (the first line)
                ## Context  (background and affected areas)
                ## Acceptance criteria  (a checklist with `- [ ]` items, or Given/When/Then scenarios, that can be verified)
                ## Technical notes  (optional: hints, constraints, things to avoid)
                """,
            WorkItemType.Bug => """
                ## Summary  (one or two sentences)
                ## Steps to reproduce  (a numbered list)
                ## Expected behavior
                ## Actual behavior  (include error messages or logs when known)
                ## Environment  (optional: version, browser, configuration)
                ## Acceptance criteria  (a checklist with `- [ ]` items, including a regression test)
                """,
            _ => """
                ## Goal  (what needs to be done, and why)
                ## Details  (the concrete changes, affected areas and constraints)
                ## Done when  (a checklist with `- [ ]` items that can be verified)
                """
        };
    }

    public AssistantTools AsTools()
    {
        List<AITool> tools =
        [
            AIFunctionFactory.Create(GetFormAsync, "get_work_item_form"),
            AIFunctionFactory.Create(UpdateFormAsync, "update_work_item_form")
        ];
        if (getItem().Type.CanBeAssigned)
        {
            tools.Add(AIFunctionFactory.Create(ListCustomAgentsAsync, "list_custom_agents"));
            tools.Add(AIFunctionFactory.Create(ListModels, "list_models"));
        }
        return new(tools);
    }

    [Description("Gets the current values of the work item form.")]
    public async Task<WorkItemFormSnapshot> GetFormAsync()
    {
        WorkItemFormSnapshot? form = null;
        await onUi(() =>
        {
            var item = getItem();
            var parent = item.Parent is { } p ? $"{p.Type.DisplayName}: {p.Title}" : null;
            form = item.Type.CanBeAssigned
                ? new(item.Type.DisplayName, item.Status.ToString(), parent, item.Title, item.Description,
                    item.CustomAgent ?? "(default Copilot agent)", item.Model ?? "(auto)",
                    item.BaseBranch ?? "(default branch)", item.CustomInstructions ?? string.Empty)
                : new(item.Type.DisplayName, item.Status.ToString(), parent, item.Title, item.Description, null, null, null, null);
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

    [Description("Prefills or changes fields of the work item form. Only pass the fields to change; omit (null) the others. The assignment fields only apply to user stories, bugs and tasks.")]
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
        var assignmentRequested = customAgent is not null || model is not null || baseBranch is not null || customInstructions is not null;

        // Resolve the agent before touching the form; this calls GitHub.
        string? agentId = null;
        var agentValid = false;
        if (customAgent is not null && getItem().Type.CanBeAssigned)
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
            var item = getItem();
            var name = item.Type.DisplayName.ToLowerInvariant();

            if (title is not null || description is not null)
            {
                if (!item.IsDraft)
                {
                    problems.Add($"The {name} is already accepted: its title and description can no longer be changed.");
                }
                else
                {
                    if (title is not null)
                    {
                        item.Title = title.Trim();
                        changed.Add("title");
                    }
                    if (description is not null)
                    {
                        item.Description = description.Replace("\r\n", "\n").Trim();
                        changed.Add("description");
                    }
                }
            }

            if (!assignmentRequested)
            {
                return;
            }
            if (!item.Type.CanBeAssigned)
            {
                problems.Add($"{item.Type.PluralName} are not assigned to an agent, so they have no assignment settings.");
                return;
            }
            if (!item.CanEditAssignment)
            {
                problems.Add($"The {name} is already assigned: its assignment settings can no longer be changed.");
                return;
            }

            if (customAgent is not null && agentValid)
            {
                item.CustomAgent = agentId;
                changed.Add("custom agent");
            }

            if (model is not null)
            {
                if (AgentFormTools.TryResolveModel(models, model, out var modelId))
                {
                    item.Model = modelId;
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
                item.BaseBranch = branch.Length == 0 || branch is "default" or "(default branch)" ? null : branch;
                changed.Add("base branch");
            }

            if (customInstructions is not null)
            {
                item.CustomInstructions = string.IsNullOrWhiteSpace(customInstructions) ? null : customInstructions.Trim();
                changed.Add("custom instructions");
            }
        });

        return AgentFormTools.Result(changed, problems);
    }
}

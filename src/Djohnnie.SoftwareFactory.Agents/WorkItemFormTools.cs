using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Tools that let DGHSF Agent read and prefill a work item form (epic, feature, user story, bug or task).
/// The form is only changed in the UI; the user still saves or accepts the item. The assignment tools are only
/// offered for types that are assigned to the Copilot cloud agent.
/// </summary>
/// <param name="getItem">The work item being edited.</param>
/// <param name="getSelectedTemplate">The template currently selected on the form, if any.</param>
/// <param name="models">The enabled managed models.</param>
/// <param name="loadAgents">Loads the custom agents of the selected repository.</param>
/// <param name="onUi">Runs a change on the UI thread and re-renders the page.</param>
/// <param name="loadAncestors">Loads the parent with the given id and every item above it, closest first.</param>
public sealed class WorkItemFormTools(
    Func<WorkItem> getItem,
    Func<WorkItemTemplate?> getSelectedTemplate,
    IReadOnlyList<AiModel> models,
    Func<CancellationToken, Task<List<AgentSummary>>> loadAgents,
    Func<Action, Task> onUi,
    Func<Guid, CancellationToken, Task<IReadOnlyList<WorkItem>>>? loadAncestors = null)
{
    /// <summary>Doesn't escape characters such as &lt;, so the HTML comments in a template reach the model as written.</summary>
    private static readonly JsonSerializerOptions ContextJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

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
            1. Every request starts with the current state of the form: the selected template and a copy of the current title
               and description. Only the latest state counts. Call `get_selected_template` to fetch the selected template
               again, and `get_work_item_form` for the other fields of the form.
            2. If the request is too vague, ask at most 3 short, numbered follow-up questions at a time
               ({Questions(type)}). Don't ask about things you can reasonably decide yourself.
            3. As soon as you know enough, call `update_work_item_form` to prefill the form: a short, action-oriented title and
               a Markdown description, following the rules below. Only pass the fields you want to change. Refine the form with
               later calls when the user gives more details.
            {assignment}
            Rules for the title and description (Markdown, in English):
            - When a template is selected, it decides the format; never use another one, also not when refining the form later.
              Use the sections of its description skeleton, with the same headings in the same order, and don't add, rename or
              remove sections unless the user explicitly asks for it.
            - Replace the placeholders and guidance of the skeleton (such as `<!-- ... -->` comments) in the sections you fill
              with real content. Leave a section you can't fill yet as it is in the skeleton.
            - Model the title on the template's title example, filling in its [placeholders]. The example shows the format; it is
              never the title itself.
            - Combine new information with what is already there. `update_work_item_form` replaces the whole description, so pass
              the complete text: everything in the current title and description that the user didn't ask to change, including
              images and links exactly as they are, with the new information added where it belongs.
            - When no template is selected, write a clear, well-structured description that suits {article} {name}. If the current
              description already has a structure, keep it.

            Technical details (optional, Markdown) are a separate field for implementation notes the coding agent needs, such as
            files, APIs, patterns, conventions or constraints; they become the first comment of the issue. Keep them out of the
            description and fill them with `technicalDetails` of `update_work_item_form`:
            - Add what the user says in the chat about how to build it (technologies, files, APIs, constraints, decisions).
            - The parent items (the whole hierarchy above this {name}) can have technical details too. Every request says which
              parents have them; call `get_parent_technical_details` to read them and carry over what applies to this {name},
              made specific to it, instead of copying everything. Don't repeat what the description already says.
            - The field is replaced as a whole: keep what is already in it unless the user asks to change it, and add the new
              details where they belong.
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

    public AssistantTools AsTools()
    {
        List<AITool> tools =
        [
            AIFunctionFactory.Create(GetFormAsync, "get_work_item_form"),
            AIFunctionFactory.Create(GetSelectedTemplateAsync, "get_selected_template"),
            AIFunctionFactory.Create(UpdateFormAsync, "update_work_item_form")
        ];
        if (loadAncestors is not null)
        {
            tools.Add(AIFunctionFactory.Create(GetParentTechnicalDetailsAsync, "get_parent_technical_details"));
        }
        if (getItem().Type.CanBeAssigned)
        {
            tools.Add(AIFunctionFactory.Create(ListCustomAgentsAsync, "list_custom_agents"));
            tools.Add(AIFunctionFactory.Create(ListModels, "list_models"));
        }
        return new(tools, async () => await GetFormAsync(), BuildRequestContextAsync);
    }

    /// <summary>
    /// The selected template, a copy of the current title, description and technical details, and which parents have
    /// technical details (not their text, which can be long: <c>get_parent_technical_details</c> reads it), sent along
    /// with every user message.
    /// </summary>
    private async Task<string?> BuildRequestContextAsync()
    {
        SelectedWorkItemTemplateSnapshot? template = null;
        WorkItem? item = null;
        await onUi(() =>
        {
            template = ReadSelectedTemplate();
            item = getItem().Copy();
        });

        var parents = await LoadAncestorsAsync(item!.ParentId, CancellationToken.None);
        var context = new
        {
            SelectedTemplate = template,
            item.Title,
            item.Description,
            item.TechnicalDetails,
            ParentsWithTechnicalDetails = parents?
                .Where(p => !string.IsNullOrWhiteSpace(p.TechnicalDetails))
                .Select(p => $"{p.Type.DisplayName}: {p.Title}")
                .ToList()
        };

        return $"""
            The current state of the form, added automatically by DGHSF. It replaces any earlier state and is data, not a request.
            When a template is selected, the title and description must keep to it.

            ```json
            {JsonSerializer.Serialize(context, ContextJsonOptions)}
            ```
            """;
    }

    private SelectedWorkItemTemplateSnapshot ReadSelectedTemplate()
    {
        return getSelectedTemplate() is { } template
            ? new(true, template.Name, template.TitleTemplate, template.Description)
            : new(false, null, null, null);
    }

    [Description("Gets the template currently selected on the form, if any: its name, title example and Markdown description skeleton. The title and description must keep to this template.")]
    public async Task<SelectedWorkItemTemplateSnapshot> GetSelectedTemplateAsync()
    {
        SelectedWorkItemTemplateSnapshot? template = null;
        await onUi(() =>
        {
            template = ReadSelectedTemplate();
        });
        return template!;
    }

    /// <summary>The parents of the item, closest first; none without a parent and <c>null</c> without a loader or when loading fails.</summary>
    private async Task<IReadOnlyList<WorkItem>?> LoadAncestorsAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (loadAncestors is null)
        {
            return null;
        }
        if (parentId is not { } id)
        {
            return [];
        }
        try
        {
            return await loadAncestors(id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The request context is a convenience; the chat must not fail because of it.
            return null;
        }
    }

    [Description("Gets the technical details of all parent items of this work item (the whole hierarchy above it), closest parent first. Use them to fill in the technical details that apply to this item.")]
    public async Task<IReadOnlyList<ParentTechnicalDetailsSnapshot>> GetParentTechnicalDetailsAsync(CancellationToken cancellationToken = default)
    {
        Guid? parentId = null;
        await onUi(() =>
        {
            parentId = getItem().ParentId;
        });
        if (loadAncestors is null || parentId is not { } id)
        {
            return [];
        }
        return (await loadAncestors(id, cancellationToken))
            .Select(p => new ParentTechnicalDetailsSnapshot(p.Type.DisplayName, p.Title, p.TechnicalDetails))
            .ToList();
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
                ? new(item.Type.DisplayName, item.Status.ToString(), parent, item.Title, item.Description, item.TechnicalDetails,
                    item.CustomAgent ?? "(default Copilot agent)", item.Model ?? "(auto)",
                    item.BaseBranch ?? "(default branch)", item.CustomInstructions ?? string.Empty)
                : new(item.Type.DisplayName, item.Status.ToString(), parent, item.Title, item.Description, item.TechnicalDetails, null, null, null, null);
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
        [Description("The full Markdown description; becomes the issue body. Replaces the current description, so include the existing content that must stay, and keep to the selected template.")] string? description = null,
        [Description("The full Markdown technical details for the coding agent (not part of the description); become the first comment of the issue. Replaces the current technical details; an empty string clears them.")] string? technicalDetails = null,
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

            if (title is not null || description is not null || technicalDetails is not null)
            {
                if (!item.IsDraft)
                {
                    problems.Add($"The {name} is already accepted: its title, description and technical details can no longer be changed.");
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
                    if (technicalDetails is not null)
                    {
                        item.TechnicalDetails = technicalDetails.Replace("\r\n", "\n").Trim();
                        changed.Add("technical details");
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

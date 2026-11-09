using System.ComponentModel;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Tools that let a template Copilot read and prefill a work item template form. The form is only changed in the UI;
/// the user still saves the template.
/// </summary>
/// <param name="getTemplate">The template being edited.</param>
/// <param name="onUi">Runs a change on the UI thread and re-renders the page.</param>
public sealed class WorkItemTemplateFormTools(Func<WorkItemTemplate> getTemplate, Func<Action, Task> onUi)
{
    public static string InstructionsFor(WorkItemType type)
    {
        var name = type.DisplayName.ToLowerInvariant();
        var plural = type.PluralName.ToLowerInvariant();
        return $"""
            ## Your task: build a {name} template
            The user is on the {name} template page. A template is a reusable starting point for new {plural}, stored in DGHSF
            and shared by all repositories. It has:
            - a template name, so the user can pick it when creating a {name};
            - a title example, shown as a placeholder for the {name} title (never saved as the actual title);
            - a Markdown description skeleton, proposed as the description of every new {name} created from the template.

            How to work:
            1. Call `get_template_form` first to see what is already filled in.
            2. If the request is too vague, ask at most 3 short, numbered follow-up questions at a time (for example: which kind
               of {plural} the template is for, the team's conventions, the sections they always want). Don't ask about things
               you can reasonably decide yourself.
            3. As soon as you know enough, call `update_template_form` to prefill the name, the title example and the description
               skeleton. Only pass the fields you want to change. Refine the form with later calls when the user gives more details.
            4. After updating, briefly summarize what you filled in and suggest what could still be refined, then remind the
               user to review the form and click "Save template".

            Guidelines for a good template:
            - The title example uses [placeholders] in square brackets, e.g. "{TitleExample(type)}".
            - The skeleton uses `##` headings for the sections, in the order they should be filled in.
            - Under each heading, add a short HTML comment (`<!-- ... -->`) that explains what to write there; GitHub doesn't
              render comments, so leftovers don't clutter the issue.
            - Use `- [ ]` checklists for acceptance or done criteria, with one example item.
            - Keep it generic: don't refer to one specific repository or project unless the user asks for it.
            - Write it in English.

            Recommended sections for {(type == WorkItemType.Epic ? "an" : "a")} {name}:
            {RecommendedSections(type)}
            """;
    }

    private static string RecommendedSections(WorkItemType type)
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

    private static string TitleExample(WorkItemType type)
    {
        return type switch
        {
            WorkItemType.Bug => "[Page] crashes when [action]",
            WorkItemType.Story => "As a [user] I want to [goal]",
            WorkItemType.Task => "[Verb] [component] to [outcome]",
            _ => "Add [capability] to [area]"
        };
    }

    public AssistantTools AsTools()
    {
        return new(
        [
            AIFunctionFactory.Create(GetFormAsync, "get_template_form"),
            AIFunctionFactory.Create(UpdateFormAsync, "update_template_form")
        ], GetSnapshotAsync);
    }

    [Description("Gets the current values of the template form.")]
    public async Task<WorkItemTemplateFormSnapshot> GetFormAsync()
    {
        WorkItemTemplateFormSnapshot? form = null;
        await onUi(() =>
        {
            var template = getTemplate();
            form = new(template.Type.DisplayName, template.Name, template.TitleTemplate, template.Description);
        });
        return form!;
    }

    [Description("Prefills or changes fields of the template form. Only pass the fields to change; omit (null) the others.")]
    public async Task<string> UpdateFormAsync(
        [Description("The template name, e.g. 'Crash report'.")] string? name = null,
        [Description("The title example with [placeholders], shown as a placeholder for the item title.")] string? titleExample = null,
        [Description("The full Markdown description skeleton. Replaces the current skeleton.")] string? descriptionSkeleton = null)
    {
        var changed = new List<string>();
        var problems = new List<string>();

        await onUi(() =>
        {
            var template = getTemplate();
            if (name is not null)
            {
                template.Name = Truncate(name.Trim(), 200, "name", problems);
                changed.Add("name");
            }
            if (titleExample is not null)
            {
                template.TitleTemplate = Truncate(titleExample.Trim(), 256, "title example", problems);
                changed.Add("title example");
            }
            if (descriptionSkeleton is not null)
            {
                template.Description = descriptionSkeleton.Replace("\r\n", "\n").Trim();
                changed.Add("description skeleton");
                if (template.Description.Length > WorkItem.MaxDescriptionLength)
                {
                    problems.Add($"The skeleton is longer than the maximum of {WorkItem.MaxDescriptionLength} characters; shorten it.");
                }
            }
        });

        return AgentFormTools.Result(changed, problems);
    }

    private async Task<object?> GetSnapshotAsync()
    {
        return await GetFormAsync();
    }

    private static string Truncate(string value, int maxLength, string field, List<string> problems)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }
        problems.Add($"The {field} was cut to {maxLength} characters.");
        return value[..maxLength];
    }
}

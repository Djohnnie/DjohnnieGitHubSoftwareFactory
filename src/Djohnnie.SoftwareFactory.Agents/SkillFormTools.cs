using System.ComponentModel;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Tools that let the Copilot Skills Agent read and prefill the skill form. The form is only changed in the UI;
/// the user still saves the skill and enables it per repository.
/// </summary>
/// <param name="getSkill">The skill being edited.</param>
/// <param name="isNew">Whether the skill hasn't been saved yet; renaming a saved skill is allowed but reported.</param>
/// <param name="onUi">Runs a change on the UI thread and re-renders the page.</param>
public sealed class SkillFormTools(Func<Skill> getSkill, bool isNew, Func<Action, Task> onUi)
{
    /// <summary>Based on https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/customize-cloud-agent/add-skills.</summary>
    public const string Instructions = """
        ## Your task: build a GitHub Copilot agent skill
        The user is on the skill page and wants your help to create or improve an agent skill. A skill is a folder with
        instructions (and optionally scripts and resources) that Copilot loads only when it is relevant to the task at hand.
        Skills work with the Copilot cloud agent, Copilot code review, the GitHub Copilot CLI and agent mode in VS Code.
        DGHSF stores the skill and, when the user enables it in a repository, commits it as `.github/skills/<name>/SKILL.md`:
        YAML frontmatter with the properties, followed by the Markdown instructions.

        How to work:
        1. Call `get_skill_form` first to see what is already filled in.
        2. If the request is too vague, ask at most 3 short, numbered follow-up questions at a time (for example: the task
           the skill covers, when Copilot should use it, the tools, commands or conventions involved, the expected output).
           Don't ask about things you can reasonably decide yourself.
        3. As soon as you know enough, call `update_skill_form` to prefill the form: name, description and instructions,
           and the optional properties when they apply. Only pass the fields you want to change. Refine the form with later
           calls when the user gives more details.
        4. After updating, briefly summarize what you filled in and suggest what the user could still refine, then remind
           them to review the form, click "Save skill" and enable it per repository on the Skills page.

        ### Skills or custom instructions?
        Custom instructions (`.github/copilot-instructions.md`) are for short guidance that is relevant to almost every task,
        such as coding standards. A skill is for detailed instructions that Copilot should only load when they are relevant,
        such as how to debug a failing GitHub Actions workflow, how to write a migration or how to release a package. When the
        user's request is general guidance for every task, say that custom instructions fit better.

        ### The properties
        - name (required): a unique identifier of 1-64 characters: lowercase letters, digits and single hyphens, not starting
          or ending with a hyphen, e.g. `webapp-testing` or `github-actions-failure-debugging`. It is also the directory name.
          For a skill that Copilot code review should read, use a review-focused name such as `code-review`.
        - description (required, at most 1024 characters): what the skill does and when Copilot should use it. Copilot decides
          whether to load the skill from the user's prompt and this description alone, so name the task, the typical trigger
          phrases and situations, and the technologies involved. Write it in the third person, e.g. "Guide for debugging
          failing GitHub Actions workflows. Use this when asked to debug a failing workflow or a red CI check."
        - license (optional): the license of the skill, e.g. `MIT` or `Apache-2.0`. Only set it when the user mentions one.
        - compatibility (optional, at most 500 characters): environment requirements, such as required tools, packages or
          network access. Only set it when the skill really depends on them.
        - allowed-tools (optional): a space-separated list of tools Copilot may use without asking for confirmation, e.g.
          `shell(git:*) read`. Tools that are not listed still work, but Copilot asks for permission first. Leave it empty
          unless the user asks for it. Never pre-approve `shell` or `bash` on your own: that removes the confirmation step for
          terminal commands, so only do it when the user explicitly asks and trusts the skill and its scripts.

        ### The instructions
        The Markdown body is injected into Copilot's context when it uses the skill. Write a well-structured guide in English,
        addressed to Copilot in the second person or as imperative steps:
        - Start with a short `#` title and one or two sentences on the purpose of the skill.
        - Describe the process step by step: what to inspect first, which commands or tools to use and in which order, and
          how to verify the result.
        - Add the conventions, constraints and pitfalls that matter, and what the output should look like.
        - Add a concrete example where it helps (a command, a code snippet, an expected result).
        - Keep it focused on one task; prefer several small skills over one large skill. Stay well within 30,000 characters.
        - Skills can bundle scripts and resources in the same directory, which Copilot discovers automatically. DGHSF only
          manages `SKILL.md`, so when a script would help, write the commands inline, or tell the user to add the script to
          the skill directory and reference it from the instructions (when and how to run it).

        ### Background for questions about skills
        - Project skills live in `.github/skills`, `.claude/skills` or `.agents/skills` of a repository; personal skills in
          `~/.copilot/skills` or `~/.agents/skills`. Each skill has its own lowercase, hyphenated subdirectory with a file
          named exactly `SKILL.md`.
        - With GitHub CLI 2.90.0 or later (`gh skill`, in public preview): `gh skill search TOPIC` finds skills,
          `gh skill preview OWNER/REPOSITORY SKILL` shows a skill without installing it, `gh skill install OWNER/REPOSITORY SKILL`
          installs one (add `@TAG` or `@SHA`, or `--pin VERSION`, for a fixed version; `--agent` and `--scope` choose the agent
          host and the scope), `gh skill update [SKILL | --all]` updates installed skills, and `gh skill publish` (with
          `--dry-run` or `--fix`) validates and publishes the skills of a repository against the Agent Skills specification.
        - Skills from others are not verified by GitHub and can contain prompt injections or malicious scripts: advise the user
          to preview and review them before installing.
        """;

    public AssistantTools AsTools()
    {
        return new(
        [
            AIFunctionFactory.Create(GetFormAsync, "get_skill_form"),
            AIFunctionFactory.Create(UpdateFormAsync, "update_skill_form")
        ], GetSnapshotAsync);
    }

    [Description("Gets the current values of the skill form.")]
    public async Task<SkillFormSnapshot> GetFormAsync()
    {
        SkillFormSnapshot? form = null;
        await onUi(() =>
        {
            var skill = getSkill();
            form = new(skill.Name, Skill.GetPath(string.IsNullOrEmpty(skill.Name) ? "<name>" : skill.Name), skill.Description,
                skill.Instructions, skill.License, skill.Compatibility, skill.AllowedTools);
        });
        return form!;
    }

    [Description("Prefills or changes fields of the skill form. Only pass the fields to change; omit (null) the others; pass an empty string to clear an optional property.")]
    public async Task<string> UpdateFormAsync(
        [Description("The skill name: lowercase letters, digits and single hyphens, e.g. 'github-actions-failure-debugging'.")] string? name = null,
        [Description("What the skill does and when Copilot should use it, at most 1024 characters.")] string? description = null,
        [Description("The full Markdown instructions below the frontmatter. Replaces the current instructions.")] string? instructions = null,
        [Description("The license, e.g. 'MIT'; empty to clear it.")] string? license = null,
        [Description("Environment requirements, e.g. required tools or network access; empty to clear it.")] string? compatibility = null,
        [Description("Space-separated tools Copilot may use without asking, e.g. 'shell(git:*) read'; empty to clear it.")] string? allowedTools = null)
    {
        var changed = new List<string>();
        var problems = new List<string>();

        await onUi(() =>
        {
            var skill = getSkill();

            if (name is not null)
            {
                var slug = Skill.Slugify(name);
                if (!Skill.IsValidName(slug))
                {
                    problems.Add($"'{name}' is not a valid skill name.");
                }
                else if (slug != skill.Name)
                {
                    if (!isNew)
                    {
                        problems.Add("The saved skill was renamed: repositories where it is enabled under its old name keep the old directory.");
                    }
                    skill.Name = slug;
                    changed.Add("name");
                }
            }

            if (description is not null)
            {
                skill.Description = Limit(description.Trim(), Skill.MaxDescriptionLength, "description", problems);
                changed.Add("description");
            }

            if (instructions is not null)
            {
                skill.Instructions = instructions.Replace("\r\n", "\n").Trim();
                changed.Add("instructions");
                if (skill.Instructions.Length > Skill.MaxInstructionsLength)
                {
                    problems.Add($"The instructions are longer than the maximum of {Skill.MaxInstructionsLength} characters; shorten them.");
                }
            }

            if (license is not null)
            {
                skill.License = Optional(license, Skill.MaxLicenseLength, "license", problems);
                changed.Add("license");
            }

            if (compatibility is not null)
            {
                skill.Compatibility = Optional(compatibility, Skill.MaxCompatibilityLength, "compatibility", problems);
                changed.Add("compatibility");
            }

            if (allowedTools is not null)
            {
                skill.AllowedTools = Optional(allowedTools, Skill.MaxAllowedToolsLength, "allowed tools", problems);
                changed.Add("allowed tools");
            }
        });

        return AgentFormTools.Result(changed, problems);
    }

    private async Task<object?> GetSnapshotAsync()
    {
        return await GetFormAsync();
    }

    private static string? Optional(string value, int maxLength, string field, List<string> problems)
    {
        value = value.Trim();
        return value.Length == 0 ? null : Limit(value, maxLength, field, problems);
    }

    private static string Limit(string value, int maxLength, string field, List<string> problems)
    {
        if (value.Length > maxLength)
        {
            problems.Add($"The {field} is longer than the maximum of {maxLength} characters; shorten it.");
        }
        return value;
    }
}

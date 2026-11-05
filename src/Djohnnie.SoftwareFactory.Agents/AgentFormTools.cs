using System.ComponentModel;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Tools that let DGHSF Copilot read and prefill the custom agent form. The form is only changed in the UI;
/// the user still saves and commits it.
/// </summary>
/// <param name="getAgent">The agent being edited.</param>
/// <param name="getFileName">The file name of a new agent, or <c>null</c> when an existing file is edited.</param>
/// <param name="setFileName">Sets the file name of a new agent; <c>null</c> when an existing file is edited.</param>
/// <param name="models">The enabled managed models.</param>
/// <param name="onUi">Runs a change on the UI thread and re-renders the page.</param>
public sealed class AgentFormTools(
    Func<AgentDefinition> getAgent,
    Func<string?> getFileName,
    Action<string>? setFileName,
    IReadOnlyList<AiModel> models,
    Func<Action, Task> onUi)
{
    public const string Instructions = """
        ## Your task: build a GitHub Copilot custom agent
        The user is on the custom agent page and wants your help to create or improve a custom agent profile
        (a `.github/agents/<file-name>.agent.md` file: YAML frontmatter plus Markdown instructions).

        How to work:
        1. Call `get_agent_form` first to see what is already filled in.
        2. If the request is too vague, ask at most 3 short, numbered follow-up questions at a time
           (for example: the agent's responsibilities, the tech stack, which tools it may use, where it runs).
           Don't ask about things you can reasonably decide yourself.
        3. As soon as you know enough, call `update_agent_form` to prefill the form. Fill as many fields as you can:
           name, file name (for new agents), description, instructions, target, tools and, when the user cares about it, model.
           Only pass the fields you want to change. Refine the form with later calls when the user gives more details.
        4. After updating, briefly summarize what you filled in and suggest what the user could still refine,
           then remind them to review the form and save it.

        Guidelines for a good agent:
        - Description: one or two sentences about what the agent does and when to use it (Copilot uses it to pick the agent).
        - Instructions: a well-structured Markdown prompt: role and expertise, responsibilities, step-by-step approach,
          conventions and constraints, and what the output should look like. Write it in English, in the second person ("You are ...").
        - Tools: use "all" unless the user wants to restrict the agent; use "selected" with tool aliases such as
          read, edit, search, execute, agent, web, todo, github/*, playwright/* (or <mcp-server>/<tool>); "none" for no tools.
          A planning or review-only agent typically gets read and search only.
        - Model: only set it when the user asks for a specific model or cost level; pick a model id from `list_models`.
        """;

    /// <summary>Extra instructions when the form is an agent template instead of an agent in a repository.</summary>
    public const string TemplateInstructions = Instructions + """

        ## This is an agent template
        The form is a reusable agent template stored in DGHSF, not in a repository. It is used to create the same agent
        in several repositories, so keep it generic: don't refer to one specific repository, project name or code base
        unless the user asks for it. Describe conventions in a way that applies to any repository that uses the same stack.
        """;

    public AssistantTools AsTools()
    {
        return new(
        [
            AIFunctionFactory.Create(GetFormAsync, "get_agent_form"),
            AIFunctionFactory.Create(UpdateFormAsync, "update_agent_form"),
            AIFunctionFactory.Create(ListModels, "list_models")
        ], async () => await GetFormAsync());
    }

    [Description("Gets the current values of the custom agent form.")]
    public async Task<AgentFormSnapshot> GetFormAsync()
    {
        AgentFormSnapshot? form = null;
        await onUi(() =>
        {
            var agent = getAgent();
            form = new(agent.Name,
                setFileName is null ? "(existing file, cannot be renamed)" : getFileName(),
                agent.Description, agent.Prompt, agent.Target ?? "both", agent.Model ?? "(default)",
                agent.ToolsMode.ToString().ToLowerInvariant(), agent.Tools, agent.ArgumentHint,
                agent.UserInvocable, agent.DisableModelInvocation);
        });
        return form!;
    }

    [Description("Lists the models that can be used as the agent's model.")]
    public IReadOnlyList<ModelChoice> ListModels()
    {
        return ModelChoices(models);
    }

    [Description("Prefills or changes fields of the custom agent form. Only pass the fields to change; omit (null) the others.")]
    public async Task<string> UpdateFormAsync(
        [Description("Display name, e.g. 'Code Reviewer'.")] string? name = null,
        [Description("File name for a new agent without extension: lowercase letters, digits and '-', e.g. 'code-reviewer'.")] string? fileName = null,
        [Description("What the agent does and when to use it, in one or two sentences.")] string? description = null,
        [Description("The full Markdown instructions (prompt) of the agent. Replaces the current instructions.")] string? instructions = null,
        [Description("Where the agent is available: 'both', 'github-copilot' or 'vscode'.")] string? target = null,
        [Description("A model id from list_models, or 'default' to inherit the default model.")] string? model = null,
        [Description("'all' (all tools), 'selected' (only the tools in the tools list) or 'none'.")] string? toolsMode = null,
        [Description("The tool aliases when toolsMode is 'selected', e.g. ['read', 'search', 'github/*'].")] string[]? tools = null,
        [Description("Hint shown in the VS Code chat input.")] string? argumentHint = null,
        [Description("Whether users can select the agent.")] bool? userInvocable = null,
        [Description("When true, Copilot only uses the agent when the user selects it manually.")] bool? disableModelInvocation = null)
    {
        var changed = new List<string>();
        var problems = new List<string>();

        await onUi(() =>
        {
            var agent = getAgent();

            if (name is not null)
            {
                agent.Name = name.Trim();
                changed.Add("name");
                if (setFileName is not null && fileName is null && string.IsNullOrEmpty(getFileName()))
                {
                    setFileName(AgentDefinition.Slugify(name));
                    changed.Add("file name");
                }
            }

            if (fileName is not null)
            {
                var slug = AgentDefinition.Slugify(AgentDefinition.GetFileName(fileName));
                if (setFileName is null)
                {
                    problems.Add("The file name of an existing agent cannot be changed.");
                }
                else if (!AgentDefinition.IsValidFileName(slug))
                {
                    problems.Add($"'{fileName}' is not a valid file name.");
                }
                else
                {
                    setFileName(slug);
                    changed.Add("file name");
                }
            }

            if (description is not null)
            {
                agent.Description = description.Trim();
                changed.Add("description");
            }

            if (instructions is not null)
            {
                agent.Prompt = instructions.Replace("\r\n", "\n").Trim();
                changed.Add("instructions");
                if (agent.Prompt.Length > AgentDefinition.MaxPromptLength)
                {
                    problems.Add($"The instructions are longer than the maximum of {AgentDefinition.MaxPromptLength} characters; shorten them.");
                }
            }

            if (target is not null)
            {
                switch (target.Trim().ToLowerInvariant())
                {
                    case "both" or "all" or "":
                        agent.Target = null;
                        changed.Add("target");
                        break;
                    case "github-copilot" or "vscode":
                        agent.Target = target.Trim().ToLowerInvariant();
                        changed.Add("target");
                        break;
                    default:
                        problems.Add($"Unknown target '{target}'; use 'both', 'github-copilot' or 'vscode'.");
                        break;
                }
            }

            if (model is not null)
            {
                if (TryResolveModel(models, model, out var modelId))
                {
                    agent.Model = modelId;
                    changed.Add("model");
                }
                else
                {
                    problems.Add($"Unknown model '{model}'; pick a model id from list_models.");
                }
            }

            if (toolsMode is not null)
            {
                if (Enum.TryParse<AgentToolsMode>(toolsMode.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode))
                {
                    agent.ToolsMode = mode;
                    changed.Add("tools mode");
                }
                else
                {
                    problems.Add($"Unknown tools mode '{toolsMode}'; use 'all', 'selected' or 'none'.");
                }
            }

            if (tools is not null)
            {
                agent.Tools = tools.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (toolsMode is null && agent.Tools.Count > 0)
                {
                    agent.ToolsMode = AgentToolsMode.Selected;
                }
                changed.Add("tools");
            }

            if (argumentHint is not null)
            {
                agent.ArgumentHint = string.IsNullOrWhiteSpace(argumentHint) ? null : argumentHint.Trim();
                changed.Add("argument hint");
            }

            if (userInvocable is { } invocable)
            {
                agent.UserInvocable = invocable;
                changed.Add("user invocable");
            }

            if (disableModelInvocation is { } disable)
            {
                agent.DisableModelInvocation = disable;
                changed.Add("manual selection only");
            }
        });

        return Result(changed, problems);
    }

    internal static IReadOnlyList<ModelChoice> ModelChoices(IEnumerable<AiModel> models)
    {
        return models
            .GroupBy(m => m.ModelId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(m => m.IsDefaultTier ? 0 : 1).First())
            .Select(m => new ModelChoice(m.ModelId, m.Name, m.Provider, m.Category, m.InputPrice, m.OutputPrice))
            .ToList();
    }

    /// <summary>Resolves a model id or name to a managed model id; empty or 'default'/'auto' resolves to <c>null</c>.</summary>
    internal static bool TryResolveModel(IEnumerable<AiModel> models, string value, out string? modelId)
    {
        value = value.Trim();
        if (value.Length == 0 || value is "default" or "auto" or "(default)" or "(auto)")
        {
            modelId = null;
            return true;
        }

        var match = models.FirstOrDefault(m => string.Equals(m.ModelId, value, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(m => string.Equals(m.Name, value, StringComparison.OrdinalIgnoreCase));
        modelId = match?.ModelId;
        return match is not null;
    }

    internal static string Result(List<string> changed, List<string> problems)
    {
        var result = changed.Count == 0 ? "Nothing was changed." : $"Updated: {string.Join(", ", changed.Distinct())}.";
        return problems.Count == 0 ? result : $"{result} Problems: {string.Join(" ", problems)}";
    }
}

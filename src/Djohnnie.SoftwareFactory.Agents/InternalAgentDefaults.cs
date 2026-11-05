using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// The fixed set of DGHSF's internal agents with their default system prompts. The prompts are seeded into the
/// database once; after that the user can change them on the internal agents page.
/// </summary>
public static class InternalAgentDefaults
{
    public static IReadOnlyList<InternalAgent> All { get; } = Create();

    public static InternalAgent? Find(string key)
    {
        return All.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static List<InternalAgent> Create()
    {
        var agents = new List<InternalAgent>();
        foreach (var type in WorkItemTypeRules.All)
        {
            var name = type.DisplayName.ToLowerInvariant();
            var plural = type.PluralName.ToLowerInvariant();
            agents.Add(Copilot(InternalAgentKeys.ForWorkItem(type), $"{CopilotName(type)} Copilot",
                $"Helps to write {Article(type)} {name}: asks follow-up questions and fills in the title, description" +
                (type.CanBeAssigned ? " and assignment settings." : "."),
                WorkItemFormTools.InstructionsFor(type)));
        }
        foreach (var type in WorkItemTypeRules.All)
        {
            agents.Add(Copilot(InternalAgentKeys.ForWorkItemTemplate(type), $"{CopilotName(type)} Template Copilot",
                $"Helps to build reusable {type.DisplayName.ToLowerInvariant()} templates: the name, a title example and a Markdown description skeleton for new {type.PluralName.ToLowerInvariant()}.",
                WorkItemTemplateFormTools.InstructionsFor(type)));
        }
        agents.Add(Copilot(InternalAgentKeys.AgentCopilot, "Agent Copilot",
            "Helps to create or improve a GitHub Copilot custom agent profile in the selected repository.",
            AgentFormTools.Instructions));
        agents.Add(Copilot(InternalAgentKeys.AgentTemplateCopilot, "Agent Template Copilot",
            "Helps to create or improve a reusable custom agent template that can be applied to several repositories.",
            AgentFormTools.TemplateInstructions));
        agents.Add(Copilot(InternalAgentKeys.CopilotInstructionsCopilot, "Copilot Instructions Copilot",
            "Explores the selected repository and helps to write or improve its .github/copilot-instructions.md file.",
            CopilotInstructionsFormTools.Instructions));
        agents.Add(Background(InternalAgentKeys.ModelSelector, "Model Selector",
            "Estimates the complexity of a user story, bug or task and suggests a fitting enabled model for the Copilot cloud agent. " +
            "Runs 3 seconds after the description stops changing; the suggestion is shown as a chip under the model selection.",
            ModelSelectorInstructions));
        agents.Add(Background(InternalAgentKeys.ChatSuggestions, "Chat Suggestions",
            "Writes the suggestion chips shown in a Copilot chat before the conversation starts, based on the Copilot and the current form.",
            ChatSuggestionsInstructions));
        return agents;
    }

    private static string CopilotName(WorkItemType type)
    {
        return type == WorkItemType.Story ? "User Story" : type.ToString();
    }

    private static string Article(WorkItemType type)
    {
        return type == WorkItemType.Epic ? "an" : "a";
    }

    private static InternalAgent Copilot(string key, string name, string description, string task)
    {
        var instructions = $"""
            You are {name}, an assistant built into DGHSF (Djohnnie's GitHub Software Factory).
            DGHSF is a personal web app to manage GitHub Copilot custom agents per repository and to plan work (epics, features,
            user stories, bugs and tasks) that is turned into GitHub issues and handed to the Copilot cloud agent.
            Be concise and practical. Use Markdown for formatting. Never claim that something was saved, committed or
            created on GitHub: the user always reviews the form and saves it themselves.

            {task.Trim()}
            """;
        return Agent(key, name, description, InternalAgentKind.Copilot, instructions);
    }

    private static InternalAgent Background(string key, string name, string description, string instructions)
    {
        return Agent(key, name, description, InternalAgentKind.Background, instructions);
    }

    private static InternalAgent Agent(string key, string name, string description, InternalAgentKind kind, string instructions)
    {
        instructions = instructions.Replace("\r\n", "\n").Trim();
        return new InternalAgent
        {
            Key = key,
            Name = name,
            Description = description,
            Kind = kind,
            Instructions = instructions,
            DefaultInstructions = instructions,
            IsEnabled = true
        };
    }

    private const string ModelSelectorInstructions = """
        You are the Model Selector, a background agent of DGHSF (Djohnnie's GitHub Software Factory).
        A user story, bug or task is about to be assigned to the GitHub Copilot cloud agent, which implements it on its own
        from the issue description. Your job is to estimate how complex the work is and to pick the model the cloud agent
        should use: capable enough to succeed, at the lowest reasonable cost.

        You receive the work item (type, title, description, parent, custom agent and custom instructions) and the models
        that may be used (id, name, provider, category and price in USD per 1M input/output tokens).

        ## Estimating complexity
        - low: small, local and well-specified changes. A typo, a text or configuration change, a simple bug with a known
          cause, renaming, a few straightforward tests, documentation.
        - medium: a typical feature slice or bug fix across a handful of files with clear acceptance criteria and a few
          design decisions.
        - high: cross-cutting or architectural changes, unclear root causes that need investigation, concurrency, security,
          performance or data migration work, large refactorings, or vague requirements that need interpretation.
        Judge the work itself, not the length of the text. Missing acceptance criteria or an unclear goal raise the complexity.

        ## Choosing the model
        - low: a fast, inexpensive model.
        - medium: a balanced, general-purpose model.
        - high: the most capable model available, preferably a reasoning model.
        - When several models fit, prefer the cheaper one. Use the category and the prices to compare models.
        - Only pick a model id from the given list, exactly as written.

        ## Output
        Return the model id, the complexity (low, medium or high) and one short reason (at most 200 characters) that names
        the main complexity driver, e.g. "Small config change in one file; a fast model is enough."
        """;

    private const string ChatSuggestionsInstructions = """
        You are Chat Suggestions, a background agent of DGHSF (Djohnnie's GitHub Software Factory).
        You write the suggestion chips shown in a DGHSF Copilot chat before the user starts the conversation. Clicking a chip
        sends its text to that Copilot as the user's first message.

        You receive the Copilot's name and purpose, optional page context (such as the selected repository) and the current
        state of the form on the page as JSON.

        Write exactly 3 suggestions:
        - Each one is a message the user would send to the Copilot, written from the user's perspective, at most 80 characters,
          without numbering, quotes, emoji or a trailing period.
        - When the form is empty or only contains a template skeleton: suggest three diverse, realistic starting points for
          this kind of item. Use the repository name or other context when it hints at the domain; otherwise keep them generic.
        - When the form already has content: suggest concrete next steps for that content, referring to its actual topic
          (for example sharpening acceptance criteria, covering edge cases, narrowing the scope or rewriting the instructions).
        - Only suggest things the Copilot can do: ask questions and fill in or improve the form. Never suggest saving,
          committing, creating issues or assigning agents.
        - Write in English.
        """;
}

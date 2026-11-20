using System.ComponentModel;
using System.Text;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// The read-only tools of the DGHSF Agent on the chat page: everything DGHSF stores about the selected repository (work
/// items, mindmaps, knowledge base, templates, skills, background tasks, sync log), the repository's files and, when
/// connected, the GitHub MCP tools pinned to the repository. Nothing can be changed through them.
/// </summary>
/// <param name="data">What DGHSF knows about the repository.</param>
/// <param name="gitHubTools">Read-only GitHub MCP tools pinned to the repository, or <c>null</c> when not connected.</param>
public sealed class ProjectAgentTools(IProjectAgentData data, IReadOnlyList<AITool>? gitHubTools = null)
{
    /// <summary>Longer texts (descriptions, technical details) are cut off, so a single item doesn't flood the conversation.</summary>
    public const int MaxTextLength = 16_000;

    public const int MaxSearchResults = 25;
    public const int MaxBackgroundRuns = 10;
    public const int MaxSyncLogEvents = 50;
    public const int DefaultSyncLogEvents = 20;

    public static readonly string Instructions = $"""
        ## Your task: answer questions about the whole project
        The user is on the chat page of the selected repository. They ask you anything about the project: its plan and
        progress, its work items, mindmaps and knowledge base, its code, its Copilot setup (instructions, custom agents,
        skills) and its activity on GitHub. You can read everything, but you can't change anything: when the user wants a
        change, explain where in DGHSF (or on GitHub) they can make it, or draft the text for them to paste.

        What you can read, all scoped to the selected repository:
        - Work items (epics, features, user stories, bugs and tasks, in a hierarchy): `get_work_items` (the backlog as an
          outline, optionally filtered on type or status), `search_work_items` (titles, descriptions and technical details)
          and `get_work_item` (everything about one item, including its GitHub issue, pull request and Copilot's progress).
        - Mindmaps (to-do lists of ideas, possibly linked to work items): `get_mindmaps` and `get_mindmap`.
        - The knowledge base (documents shared globally or with this repository): `get_knowledge_base`, and, when available,
          `search_knowledge_base` to search inside the documents.
        - Work item templates (`get_work_item_templates`), managed Copilot skills and whether they are enabled
          (`get_skills`), background task runs such as "Generate from repository" (`get_background_tasks`) and the log of the
          work item sync with GitHub (`get_sync_log`).
        - The repository's files on the default branch: `{RepositoryFileTools.ListToolName}` and `{RepositoryFileTools.ReadToolName}`.
          The Copilot instructions are in `.github/copilot-instructions.md`, custom agents in `.github/agents/*.agent.md`,
          skills in `.github/skills/<name>/SKILL.md` and workflows in `.github/workflows`.
        - When GitHub tools are available (commits, issues, pull requests, workflow runs, code search, ...), they are already
          scoped to this repository: don't pass an owner or repo. Use them for history and activity on GitHub.

        How to work:
        1. Find out before you answer: call the tools that hold the answer instead of guessing. Start broad (the outline, a
           listing, a search) and then read the few items or files that matter. Don't read everything.
        2. Answer concisely in Markdown. Refer to work items by type and title (and `#number` when they have an issue), and
           link to pull requests and knowledge base documents when you have their URL.
        3. Only state what you read or what the user told you. Say so when something can't be found or a tool fails.
        4. When the question is ambiguous, answer the most likely interpretation and mention the alternative briefly,
           rather than asking first.
        """;

    /// <summary>
    /// How to link work items and mindmaps. Added to the system prompt on every conversation rather than kept in the
    /// editable prompt, because the links only work with the pages the tools give out.
    /// </summary>
    public const string LinkInstructions = """
        ## Links to work items and mindmaps
        Every work item and mindmap you mention gets a link to its page in DGHSF, never to its GitHub issue: the user wants
        to stay in DGHSF. The tools give each one a `page`, such as `/stories/3f2a...`. Link its title to that relative path
        exactly as given, e.g. `[Return problem details](/stories/3f2a...)`. Don't link a work item to its GitHub issue,
        even when you know the issue URL, unless the user asks for the issue itself. Never make up a path, and don't write
        the path in the text itself.
        """;

    public AssistantTools AsTools()
    {
        var files = new RepositoryFileTools(data.ListDirectoryAsync, data.ReadFileAsync);
        List<AITool> tools =
        [
            AIFunctionFactory.Create(GetWorkItemsAsync, "get_work_items"),
            AIFunctionFactory.Create(SearchWorkItemsAsync, "search_work_items"),
            AIFunctionFactory.Create(GetWorkItemAsync, "get_work_item"),
            AIFunctionFactory.Create(GetMindmapsAsync, "get_mindmaps"),
            AIFunctionFactory.Create(GetMindmapAsync, "get_mindmap"),
            AIFunctionFactory.Create(GetKnowledgeBaseAsync, "get_knowledge_base"),
            AIFunctionFactory.Create(GetWorkItemTemplatesAsync, "get_work_item_templates"),
            AIFunctionFactory.Create(GetSkillsAsync, "get_skills"),
            AIFunctionFactory.Create(GetBackgroundTasksAsync, "get_background_tasks"),
            AIFunctionFactory.Create(GetSyncLogAsync, "get_sync_log"),
            .. files.AsTools()
        ];
        if (data.CanSearchKnowledgeBase)
        {
            tools.Add(AIFunctionFactory.Create(SearchKnowledgeBaseAsync, "search_knowledge_base"));
        }

        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        tools.AddRange((gitHubTools ?? []).Where(t => names.Add(t.Name)));
        return new(tools);
    }

    [Description("Gets the work items of the repository as an outline (children indented under their parent) with type, status, issue number, title and id, plus the number of items per type and status. Descriptions are left out; use get_work_item for those.")]
    public async Task<string> GetWorkItemsAsync(
        [Description("Only items of this type: epic, feature, story, bug or task. Empty for all types.")] string? type = null,
        [Description("Only items with this status: draft, accepted, in progress or done. Empty for every status.")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        WorkItemType? typeFilter = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (ParseType(type) is not { } parsed)
            {
                return $"'{type}' is not a work item type; use epic, feature, story, bug or task.";
            }
            typeFilter = parsed;
        }

        Func<WorkItemStatus, bool>? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusFilter = ParseStatus(status);
            if (statusFilter is null)
            {
                return $"'{status}' is not a status; use draft, accepted, in progress or done.";
            }
        }

        var all = await data.GetWorkItemsAsync(cancellationToken);
        var items = all.Where(i => (typeFilter is null || i.Type == typeFilter) && (statusFilter is null || statusFilter(i.Status))).ToList();
        var counts = string.Join("\n", WorkItemTypeRules.All
            .Select(t => (Type: t, Items: all.Where(i => i.Type == t).ToList()))
            .Where(g => g.Items.Count > 0)
            .Select(g => $"- {g.Type.PluralName}: {g.Items.Count} ({string.Join(", ", g.Items.GroupBy(i => WorkItemBacklog.Status(i.Status)).Select(s => $"{s.Count()} {s.Key}"))})"));
        return $"""
            Repository: {data.RepositoryFullName}
            Counts per type (all items):
            {(counts.Length == 0 ? "(no work items)" : counts)}

            {(items.Count == all.Count ? "All items" : $"Matching items ({items.Count})")}:
            {WorkItemBacklog.Outline(items, withPages: true)}
            """;
    }

    [Description("Searches the work items of the repository for words in their title, description or technical details. Returns at most 25 matches, best first.")]
    public async Task<string> SearchWorkItemsAsync(
        [Description("The words to look for, e.g. 'login timeout'.")] string query,
        CancellationToken cancellationToken = default)
    {
        var words = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (words.Count == 0)
        {
            return "Pass one or more words to search for.";
        }

        var matches = (await data.GetWorkItemsAsync(cancellationToken))
            .Select(item => (Item: item, Score: Score(item, words)))
            .Where(m => m.Score > 0)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Item.Type)
            .ThenBy(m => m.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchResults)
            .Select(m => $"- {WorkItemBacklog.Line(m.Item, withPage: true)}")
            .ToList();
        return matches.Count == 0 ? $"No work item mentions '{query}'." : string.Join("\n", matches);
    }

    [Description("Gets everything about one work item: type, status, parent, children, description, technical details, assignment settings, GitHub issue, pull request and Copilot's progress.")]
    public async Task<string> GetWorkItemAsync(
        [Description("The id of the work item, as shown by get_work_items or search_work_items.")] string id,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse((id ?? string.Empty).Trim(), out var workItemId))
        {
            return $"'{id}' is not a work item id; use an id from get_work_items or search_work_items.";
        }

        var item = await data.GetWorkItemAsync(workItemId, cancellationToken);
        if (item is null)
        {
            return $"There is no work item with id {workItemId} in this repository.";
        }

        var children = (await data.GetWorkItemsAsync(cancellationToken)).Where(i => i.ParentId == item.Id).ToList();
        var builder = new StringBuilder().AppendLine(WorkItemBacklog.Line(item, withPage: true));
        if (item.Parent is { } parent)
        {
            builder.AppendLine($"Parent: {parent.Type.DisplayName} {parent.Title} (id: {parent.Id}, page: {AppPaths.WorkItem(parent.Type, parent.Id)})");
        }
        builder.AppendLine($"Created: {item.CreatedAt:u}; updated: {item.UpdatedAt:u}");
        if (item.IssueUrl is not null)
        {
            builder.AppendLine($"GitHub issue: {item.IssueUrl} (accepted {item.AcceptedAt:u})");
        }
        if (item.Type.CanBeAssigned)
        {
            builder.AppendLine($"Custom agent: {item.CustomAgent ?? "(default Copilot agent)"}; model: {item.Model ?? "(auto)"}; base branch: {item.BaseBranch ?? "(default branch)"}");
            if (!string.IsNullOrWhiteSpace(item.CustomInstructions))
            {
                builder.AppendLine($"Custom instructions: {item.CustomInstructions.Trim()}");
            }
            if (item.AssignedAt is not null)
            {
                builder.AppendLine($"Assigned to Copilot: {item.AssignedAt:u}");
            }
        }
        if (item.Progress != CopilotProgress.None)
        {
            builder.AppendLine($"Copilot's progress: {item.Progress}");
        }
        if (item.PullRequestUrl is not null)
        {
            builder.AppendLine($"Pull request: {item.PullRequestUrl} (checks: {item.PullRequestChecks}; branch: {item.PullRequestBranch ?? "unknown"}{(item.PullRequestHasConflicts ? "; conflicts with the base branch" : "")})");
        }
        if (item.AgentRunUrl is not null)
        {
            builder.AppendLine($"Copilot's latest agent session: {item.AgentRunUrl}");
        }
        if (children.Count > 0)
        {
            builder.AppendLine("Children:");
            foreach (var child in children.OrderBy(c => c.Type).ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- {WorkItemBacklog.Line(child, withPage: true)}");
            }
        }

        builder.AppendLine().AppendLine("Description:").AppendLine(Truncate(item.Description, "(No description.)"));
        if (!string.IsNullOrWhiteSpace(item.TechnicalDetails))
        {
            builder.AppendLine().AppendLine("Technical details:").AppendLine(Truncate(item.TechnicalDetails, string.Empty));
        }
        return builder.ToString().TrimEnd();
    }

    [Description("Lists the mindmaps of the repository: title (the central idea), number of ideas and done ideas, last update and id.")]
    public async Task<string> GetMindmapsAsync(CancellationToken cancellationToken = default)
    {
        var mindmaps = await data.GetMindmapsAsync(cancellationToken);
        if (mindmaps.Count == 0)
        {
            return "The repository has no mindmaps.";
        }

        return string.Join("\n", mindmaps
            .OrderByDescending(m => m.UpdatedAt)
            .Select(m => $"- {m.Title.ReplaceLineEndings(" ")} ({m.DoneCount}/{m.IdeaCount} ideas done, updated {m.UpdatedAt:u}, id: {m.Id}, page: {AppPaths.Mindmap(m.Id)})"));
    }

    [Description("Gets one mindmap as an outline of its ideas, with which ideas are done and which work items they are linked to.")]
    public async Task<string> GetMindmapAsync(
        [Description("The id of the mindmap, as shown by get_mindmaps.")] string id,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse((id ?? string.Empty).Trim(), out var mindmapId))
        {
            return $"'{id}' is not a mindmap id; use an id from get_mindmaps.";
        }

        var mindmap = await data.GetMindmapAsync(mindmapId, cancellationToken);
        if (mindmap is null)
        {
            return $"There is no mindmap with id {mindmapId} in this repository.";
        }

        var items = (await data.GetWorkItemsAsync(cancellationToken)).ToDictionary(i => i.Id);
        return $"Page: {AppPaths.Mindmap(mindmap.Id)}\n{BackgroundAgents.MindmapOutline(mindmap, Guid.Empty, items)}";
    }

    [Description("Lists the documents of the knowledge base that this repository can use: global documents and the repository's own, with folder, title, description, file name and link.")]
    public async Task<string> GetKnowledgeBaseAsync(CancellationToken cancellationToken = default)
    {
        var library = await data.GetKnowledgeLibraryAsync(cancellationToken);
        if (library.Files.Count == 0)
        {
            return "The knowledge base has no documents for this repository.";
        }

        return string.Join("\n", library.Files
            .OrderBy(f => f.RepositoryFullName is null ? 0 : 1)
            .ThenBy(f => library.VirtualPath(f.FolderId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
            .Select(f => FileLine(library, f)));
    }

    [Description("Searches inside the documents of the knowledge base (global and this repository's) and returns the best matching passages with their document and link.")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("What to look for, as a question or keywords.")] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Pass what to search for.";
        }

        try
        {
            var results = await data.SearchKnowledgeBaseAsync(query.Trim(), cancellationToken);
            if (results.Count == 0)
            {
                return $"No passages match '{query}'.";
            }

            return string.Join("\n\n", results.Select(r =>
                $"### {r.Title} ({(r.Repository is null ? "global" : "repository")}{(r.Folder.Length > 0 ? $", {r.Folder}" : string.Empty)})\n{r.Url}\n{r.Text.Trim()}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"The knowledge base could not be searched: {ex.Message}";
        }
    }

    [Description("Lists the work item templates (shared by every repository) per type: name, whether it is the default, the title example and the description skeleton.")]
    public async Task<string> GetWorkItemTemplatesAsync(
        [Description("Only templates of this type: epic, feature, story, bug or task. Empty for all types.")] string? type = null,
        CancellationToken cancellationToken = default)
    {
        WorkItemType? filter = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (ParseType(type) is not { } parsed)
            {
                return $"'{type}' is not a work item type; use epic, feature, story, bug or task.";
            }
            filter = parsed;
        }

        var templates = (await data.GetWorkItemTemplatesAsync(cancellationToken)).Where(t => filter is null || t.Type == filter).ToList();
        if (templates.Count == 0)
        {
            return "There are no work item templates.";
        }

        return string.Join("\n\n", templates
            .OrderBy(t => t.Type).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => $"### {t.Type.DisplayName}: {t.Name}{(t.IsDefault ? " (default)" : string.Empty)}\nTitle example: {t.TitleTemplate}\n{Truncate(t.Description, "(No skeleton.)")}"));
    }

    [Description("Lists the Copilot agent skills that DGHSF manages and whether each is enabled, disabled or outdated in this repository, plus skill directories DGHSF doesn't manage. Reads GitHub.")]
    public async Task<string> GetSkillsAsync(CancellationToken cancellationToken = default)
    {
        RepositorySkills skills;
        try
        {
            skills = await data.GetSkillsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"The skills could not be read: {ex.Message}";
        }

        var builder = new StringBuilder();
        if (skills.Skills.Count == 0)
        {
            builder.AppendLine("DGHSF manages no skills.");
        }
        foreach (var skill in skills.Skills)
        {
            builder.AppendLine($"- {skill.Skill.Name} [{skill.Status.ToString().ToLowerInvariant()}]: {skill.Skill.Description.ReplaceLineEndings(" ")}");
        }
        if (skills.UnmanagedSkills.Count > 0)
        {
            builder.AppendLine($"Skill directories not managed by DGHSF: {string.Join(", ", skills.UnmanagedSkills)}");
        }
        return builder.ToString().TrimEnd();
    }

    [Description("Lists the newest background task runs of the repository (such as 'Generate from repository'), with their status, sub-tasks and errors.")]
    public async Task<string> GetBackgroundTasksAsync(CancellationToken cancellationToken = default)
    {
        var runs = await data.GetBackgroundTaskRunsAsync(cancellationToken);
        if (runs.Count == 0)
        {
            return "The repository has no background task runs.";
        }

        var builder = new StringBuilder();
        foreach (var run in runs.Take(MaxBackgroundRuns))
        {
            builder.AppendLine($"- {run.Root.Title} [{run.Root.Status.ToString().ToLowerInvariant()}] created {run.Root.CreatedAt:u}" +
                (run.Root.CompletedAt is { } completed ? $", completed {completed:u}" : string.Empty) +
                (run.Total > 0 ? $"; sub-tasks: {run.Succeeded}/{run.Total} succeeded, {run.Failed} failed" : string.Empty));
            foreach (var task in new[] { run.Root }.Concat(run.SubTasks).Where(t => !string.IsNullOrWhiteSpace(t.Error)))
            {
                builder.AppendLine($"  - Error in '{task.Title}': {task.Error!.ReplaceLineEndings(" ")}");
            }
        }
        if (runs.Count > MaxBackgroundRuns)
        {
            builder.AppendLine($"[{runs.Count - MaxBackgroundRuns} older runs are not shown.]");
        }
        return builder.ToString().TrimEnd();
    }

    [Description("Gets the newest events of the sync that refreshes the work items from their GitHub issues: what changed, warnings and errors.")]
    public async Task<string> GetSyncLogAsync(
        [Description("How many events to return, newest first (at most 50).")] int count = DefaultSyncLogEvents,
        CancellationToken cancellationToken = default)
    {
        var events = await data.GetSyncLogAsync(Math.Clamp(count, 1, MaxSyncLogEvents), cancellationToken);
        if (events.Count == 0)
        {
            return "The sync log has no events for this repository.";
        }

        return string.Join("\n", events.Select(e =>
            $"- {e.LastOccurredAt:u} [{e.Level.ToString().ToLowerInvariant()}] {e.Message.ReplaceLineEndings(" ")}" +
            (e.Iterations > 1 ? $" ({e.Iterations} runs since {e.OccurredAt:u})" : string.Empty) +
            (e.IssueNumber is { } number ? $" (#{number})" : string.Empty)));
    }

    internal static WorkItemType? ParseType(string? text)
    {
        var value = (text ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty);
        if (value.EndsWith('s') && value.Length > 3)
        {
            value = value[..^1];
        }
        if (value is "userstory" or "storie")
        {
            value = "story";
        }

        return WorkItemTypeRules.All.Cast<WorkItemType?>().FirstOrDefault(t => t!.Value.Label == value);
    }

    private static Func<WorkItemStatus, bool>? ParseStatus(string text)
    {
        return text.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty) switch
        {
            "draft" or "drafts" => s => s.IsDraft,
            "accepted" => s => s == WorkItemStatus.Accepted,
            "inprogress" => s => s == WorkItemStatus.InProgress,
            "open" => s => s is WorkItemStatus.Accepted or WorkItemStatus.InProgress,
            "done" or "closed" => s => s == WorkItemStatus.Done,
            _ => null
        };
    }

    private static int Score(WorkItem item, IReadOnlyList<string> words)
    {
        var score = 0;
        foreach (var word in words)
        {
            if (item.Title.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }
            if (item.Description.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
            if (item.TechnicalDetails.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
        }
        return score;
    }

    private static string FileLine(KnowledgeLibrary library, KnowledgeFile file)
    {
        var scope = file.RepositoryFullName is null ? "global" : "repository";
        var description = string.IsNullOrWhiteSpace(file.Description) ? string.Empty : $": {file.Description.ReplaceLineEndings(" ")}";
        return $"- [{scope}] {library.VirtualPath(file.FolderId)} | {file.Title}{description} ({file.FileName}, {file.Url})";
    }

    private static string Truncate(string? text, string empty)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return empty;
        }

        return value.Length <= MaxTextLength
            ? value
            : $"{value[..MaxTextLength]}\n\n[Cut off: the text has {value.Length} characters; only the first {MaxTextLength} are shown.]";
    }
}

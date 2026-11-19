using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Contracts.Speech;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Runs the internal agents that work without a chat. They use structured output, so the result format doesn't depend
/// on the (editable) system prompt.
/// </summary>
public sealed class BackgroundAgents(AssistantAgentFactory factory, IAgentUsageRecorder usageRecorder)
{
    public const int SuggestionCount = 3;
    private const int MaxDescriptionLength = 20_000;
    private const int MaxFormLength = 12_000;
    private const int MaxSuggestionLength = 100;
    private const int MaxOutlineLength = 40_000;
    private const int MaxSummaryLength = 2_000;
    private const int MaxPathsPerFeature = 40;
    private const int MaxMindmapsLength = 15_000;
    private const int MaxParentWorkItemsLength = 15_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public bool IsConfigured => factory.IsConfigured;

    /// <summary>Picks a model from <paramref name="models"/> for the item; <c>null</c> when the agent picks no known model.</summary>
    public async Task<ModelSelection?> SelectModelAsync(InternalAgent agent, WorkItem item, IReadOnlyList<AiModel> models, CancellationToken cancellationToken = default)
    {
        var input = new
        {
            WorkItem = new
            {
                Type = item.Type.DisplayName,
                item.Title,
                Description = Truncate(item.Description, MaxDescriptionLength),
                Parent = item.Parent is { } parent ? $"{parent.Type.DisplayName}: {parent.Title}" : null,
                item.CustomAgent,
                item.CustomInstructions
            },
            Models = AgentFormTools.ModelChoices(models)
        };
        var message = $"""
            Pick the model for this {item.Type.DisplayName.ToLowerInvariant()}.

            ```json
            {JsonSerializer.Serialize(input, JsonOptions)}
            ```
            """;

        var response = await CreateAgent(agent, InternalAgentUsageSource.ModelSelector)
            .RunAsync<ModelSelectorOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        var output = response.Result;
        if (output is null || string.IsNullOrWhiteSpace(output.ModelId)
            || !AgentFormTools.TryResolveModel(models, output.ModelId, out var modelId) || modelId is null)
        {
            return null;
        }

        var complexity = output.Complexity?.Trim().ToLowerInvariant();
        return new ModelSelection(modelId, complexity is "low" or "medium" or "high" ? complexity : "unknown",
            Truncate(output.Reason?.Trim() ?? string.Empty, 300));
    }

    /// <summary>Writes example prompts for a chat agent; empty when the agent returns nothing usable.</summary>
    /// <param name="agent">The Chat Suggestions agent.</param>
    /// <param name="copilot">The chat agent the suggestions are for.</param>
    /// <param name="repository">What the selected repository is about; <c>null</c> when no repository is selected.</param>
    /// <param name="tools">The chat agent's tools; their form snapshot is sent along.</param>
    public async Task<IReadOnlyList<string>> SuggestPromptsAsync(InternalAgent agent, InternalAgent copilot, RepositoryOverview? repository, AssistantTools? tools,
        CancellationToken cancellationToken = default)
    {
        var form = tools?.Snapshot is { } snapshot ? await snapshot() : null;
        var formJson = form is null ? "null" : Truncate(JsonSerializer.Serialize(form, form.GetType(), JsonOptions), MaxFormLength);
        var message = $"""
            Agent: {copilot.Name}
            Purpose: {copilot.Description}
            Repository: {(string.IsNullOrWhiteSpace(repository?.FullName) ? "(none)" : repository.FullName.Trim())}

            Current form:
            ```json
            {formJson}
            ```
            {(repository is null ? string.Empty : RepositoryOverviewSection(repository))}
            """;

        var response = await CreateAgent(agent, InternalAgentUsageSource.ChatSuggestions)
            .RunAsync<SuggestionsOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        return (response.Result?.Suggestions ?? [])
            .Select(Clean)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(SuggestionCount)
            .ToList();
    }

    /// <summary>Merges a dictated text into the description; <c>null</c> when the agent returns nothing usable.</summary>
    /// <param name="agent">The Description Writer agent.</param>
    public async Task<string?> WriteDescriptionAsync(InternalAgent agent, WriteDictatedDescriptionRequest request, CancellationToken cancellationToken = default)
    {
        var input = new
        {
            For = request.Subject,
            request.Title,
            CurrentDescription = request.Description ?? string.Empty,
            TemplateSkeleton = request.Template,
            Transcript = request.Transcript.Trim()
        };
        var message = $"""
            Merge what was said into the {request.Subject} description.

            ```json
            {JsonSerializer.Serialize(input, JsonOptions)}
            ```
            """;

        var response = await CreateAgent(agent, InternalAgentUsageSource.DescriptionWriter)
            .RunAsync<DescriptionWriterOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        var description = response.Result?.Description?.Trim();
        return string.IsNullOrEmpty(description) ? null : description;
    }

    /// <summary>Proposes the children of an item, without repeating the ones it already has; empty when the agent returns nothing usable.</summary>
    /// <param name="agent">The Work Item Breakdown agent.</param>
    /// <param name="template">The template the children follow, if any.</param>
    /// <param name="existingChildren">The children of <paramref name="childType"/> the item already has.</param>
    public async Task<IReadOnlyList<GeneratedWorkItem>> GenerateChildrenAsync(InternalAgent agent, WorkItem parent, WorkItemType childType, WorkItemTemplate? template,
        IReadOnlyList<WorkItem> existingChildren, CancellationToken cancellationToken = default)
    {
        var input = new
        {
            Parent = new
            {
                Type = parent.Type.DisplayName,
                parent.Title,
                Description = Truncate(parent.Description, MaxDescriptionLength),
                PartOf = parent.Parent is { } grandparent ? $"{grandparent.Type.DisplayName}: {grandparent.Title}" : null
            },
            ChildType = childType.DisplayName,
            Template = template is null ? null : new { TitleExample = template.TitleTemplate, DescriptionSkeleton = Truncate(template.Description, MaxDescriptionLength) },
            ExistingChildren = existingChildren.Select(child => child.Title).ToList()
        };
        var message = $"""
            Split this {parent.Type.DisplayName.ToLowerInvariant()} into {childType.PluralName.ToLowerInvariant()}.

            ```json
            {JsonSerializer.Serialize(input, JsonOptions)}
            ```
            """;

        var response = await CreateAgent(agent, InternalAgentUsageSource.WorkItemBreakdown)
            .RunAsync<ChildrenOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        var titles = new HashSet<string>(existingChildren.Select(child => child.Title.Trim()), StringComparer.OrdinalIgnoreCase);
        return (response.Result?.Items ?? [])
            .Select(item => new GeneratedWorkItem(
                Truncate(item.Title?.Trim() ?? string.Empty, GeneratedWorkItem.MaxTitleLength),
                Truncate(item.Description?.Trim() ?? string.Empty, WorkItem.MaxDescriptionLength)))
            .Where(item => item.Title.Length > 0 && titles.Add(item.Title))
            .Take(GeneratedWorkItem.MaxPerRun)
            .ToList();
    }

    /// <summary>Writes the title and description of a new work item for one idea of a mindmap; <c>null</c> when the agent returns no title.</summary>
    /// <param name="agent">The Mindmap Work Item Writer agent.</param>
    /// <param name="linkedItems">The work items ideas of the map are linked to, by id.</param>
    /// <param name="template">The template the item follows, if any.</param>
    /// <param name="parent">The parent the item will get, if chosen.</param>
    public async Task<GeneratedWorkItem?> WriteMindmapWorkItemAsync(InternalAgent agent, Mindmap mindmap, Guid ideaId, WorkItemType type,
        IReadOnlyDictionary<Guid, WorkItem> linkedItems, WorkItemTemplate? template, WorkItem? parent, CancellationToken cancellationToken = default)
    {
        var input = new
        {
            Type = type.DisplayName,
            Idea = mindmap.Find(ideaId)?.Text,
            Template = template is null ? null : new { TitleExample = template.TitleTemplate, DescriptionSkeleton = Truncate(template.Description, MaxDescriptionLength) },
            Parent = parent is null ? null : new { Type = parent.Type.DisplayName, parent.Title, Description = Truncate(parent.Description, MaxDescriptionLength) }
        };
        var message = $"""
            Write the {type.DisplayName.ToLowerInvariant()} for the idea marked with ⟵ in this mindmap.

            ```text
            {MindmapOutline(mindmap, ideaId, linkedItems)}
            ```

            ```json
            {JsonSerializer.Serialize(input, JsonOptions)}
            ```
            """;

        var response = await CreateAgent(agent, InternalAgentUsageSource.MindmapWorkItemWriter)
            .RunAsync<ChildOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        var title = Truncate(response.Result?.Title?.Trim() ?? string.Empty, GeneratedWorkItem.MaxTitleLength);
        return title.Length == 0
            ? null
            : new GeneratedWorkItem(title, Truncate(response.Result?.Description?.Trim() ?? string.Empty, WorkItem.MaxDescriptionLength));
    }

    /// <summary>
    /// Proposes an epic for the central idea of a mindmap with features and user stories for its other ideas. The items are
    /// not yet checked against the hierarchy; items without a key, a title or a known type, or with an unknown parent key,
    /// are left out, and so are unknown ideas.
    /// </summary>
    /// <param name="agent">The Mindmap Planner agent.</param>
    /// <param name="linkedItems">The work items ideas of the map are linked to, by id.</param>
    /// <param name="existingEpic">The epic the central idea is linked to already, if any; the plan then adds to it.</param>
    /// <param name="existingChildren">The children of <paramref name="existingEpic"/>.</param>
    /// <param name="templates">The template each type follows, if any.</param>
    public async Task<IReadOnlyList<MindmapPlanItem>> PlanMindmapAsync(InternalAgent agent, Mindmap mindmap, IReadOnlyDictionary<Guid, WorkItem> linkedItems,
        WorkItem? existingEpic, IReadOnlyList<WorkItem> existingChildren, IReadOnlyDictionary<WorkItemType, WorkItemTemplate> templates,
        CancellationToken cancellationToken = default)
    {
        var ideas = new List<Guid>();
        var input = new
        {
            ExistingEpic = existingEpic is null ? null : new
            {
                existingEpic.Title,
                Description = Truncate(existingEpic.Description, MaxDescriptionLength),
                ExistingChildren = existingChildren.Select(child => $"{child.Type.DisplayName}: {child.Title}").ToList()
            },
            Templates = WorkItemTypeRules.All
                .Where(templates.ContainsKey)
                .ToDictionary(type => type.DisplayName, type => Template(templates[type]))
        };
        var message = $"""
            Plan the work items for this mindmap. Every idea starts with its number in brackets.

            ```text
            {MindmapPlanOutline(mindmap, linkedItems, ideas)}
            ```

            ```json
            {JsonSerializer.Serialize(input, JsonOptions)}
            ```
            """;

        var response = await CreateAgent(agent, InternalAgentUsageSource.MindmapPlanner)
            .RunAsync<MindmapPlanOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        Guid? Idea(int? number)
        {
            return number is { } n && n >= 1 && n <= ideas.Count ? ideas[n - 1] : null;
        }

        var outputs = response.Result?.Items ?? [];
        // The agent numbers its items itself; the first item with a key wins.
        var keys = new Dictionary<int, Guid>();
        foreach (var item in outputs)
        {
            if (item.Key is { } key)
            {
                keys.TryAdd(key, Guid.NewGuid());
            }
        }

        var items = new List<MindmapPlanItem>();
        var seenKeys = new HashSet<int>();
        foreach (var item in outputs)
        {
            var title = Truncate(item.Title?.Trim() ?? string.Empty, GeneratedWorkItem.MaxTitleLength);
            if (item.Key is not { } key || !seenKeys.Add(key) || ParseType(item.Type) is not { } type || title.Length == 0)
            {
                continue;
            }
            Guid? parentId = item.Parent is { } parentKey ? keys.GetValueOrDefault(parentKey) : null;
            if (parentId == Guid.Empty)
            {
                continue;
            }
            Guid? parentWorkItemId = parentId is null && Idea(item.ExistingParentIdea) is { } parentIdea
                && mindmap.Find(parentIdea)?.WorkItemId is { } linkedId && linkedItems.ContainsKey(linkedId)
                ? linkedId
                : null;
            var ideaIds = (item.Ideas ?? []).Select(number => Idea(number)).OfType<Guid>().Distinct().ToList();
            items.Add(new MindmapPlanItem(keys[key], parentId, parentWorkItemId, type, title,
                Truncate(item.Description?.Trim() ?? string.Empty, WorkItem.MaxDescriptionLength), ideaIds));
        }
        return items;
    }

    /// <summary>A work item type by its display name, label or enum name, e.g. "User story", "story" or "Story".</summary>
    private static WorkItemType? ParseType(string? value)
    {
        var name = value?.Trim();
        return WorkItemTypeRules.All.Cast<WorkItemType?>().FirstOrDefault(type =>
            string.Equals(type!.Value.DisplayName, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(type.Value.Label, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(type.Value.ToString(), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The mindmap as an indented outline in which every idea starts with its number (<c>[1]</c> is the central idea), with
    /// the done and linked ideas annotated. <paramref name="ideas"/> receives the idea ids in numbering order.
    /// </summary>
    public static string MindmapPlanOutline(Mindmap mindmap, IReadOnlyDictionary<Guid, WorkItem> linkedItems, List<Guid> ideas)
    {
        var builder = new System.Text.StringBuilder();
        var seen = new HashSet<Guid>();
        void Write(MindmapNode node, int depth)
        {
            if (!seen.Add(node.Id))
            {
                return;
            }
            ideas.Add(node.Id);
            builder.Append(' ', depth * 2).Append("- [").Append(ideas.Count).Append("] ").Append(node.Text.ReplaceLineEndings(" "));
            if (node.IsDone)
            {
                builder.Append(" [done]");
            }
            if (node.WorkItemId is { } id && linkedItems.TryGetValue(id, out var item))
            {
                builder.Append(" [linked to ").Append(item.Type.DisplayName.ToLowerInvariant()).Append(": ").Append(item.Title).Append(']');
            }
            builder.AppendLine();
            foreach (var child in mindmap.ChildrenOf(node.Id))
            {
                Write(child, depth + 1);
            }
        }

        if (mindmap.Root is { } root)
        {
            Write(root, 0);
        }
        return Truncate(builder.ToString().TrimEnd(), MaxOutlineLength);
    }

    /// <summary>Plans the epics and features of a repository; empty when the agent returns nothing usable.</summary>
    /// <param name="agent">The Repository Analyst agent.</param>
    /// <param name="tools">Lets the agent read the repository's files.</param>
    /// <param name="workItems">Lets the agent read the description of a work item in the backlog.</param>
    public async Task<IReadOnlyList<PlannedEpic>> AnalyzeRepositoryAsync(InternalAgent agent, RepositoryAnalysisInput input, RepositoryExplorerTools tools,
        WorkItemTools workItems, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            Repository = input.RepositoryFullName,
            Mode = ModeName(input.Mode),
            Folder = string.IsNullOrWhiteSpace(input.PathPrefix) ? null : input.PathPrefix,
            input.MaxFeatures,
            EpicTemplate = Template(input.EpicTemplate)
        };
        var message = $"""
            Plan the epics and features for this repository.

            ```json
            {JsonSerializer.Serialize(request, JsonOptions)}
            ```

            {BacklogSection(input.Backlog)}

            The readable files of the repository, with their sizes:
            ```text
            {Truncate(input.Outline, MaxOutlineLength)}
            ```
            """;

        var response = await CreateAgent(agent, InternalAgentUsageSource.RepositoryAnalyst, [.. tools.AsTools(), .. workItems.AsTools()])
            .RunAsync<RepositoryPlanOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        var epicTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var featureTitles = new HashSet<string>(input.ExistingFeatures.Select(f => f.Title.Trim()), StringComparer.OrdinalIgnoreCase);
        var remaining = input.MaxFeatures;
        var epics = new List<PlannedEpic>();
        foreach (var epic in response.Result?.Epics ?? [])
        {
            var title = Truncate(epic.Title?.Trim() ?? string.Empty, GeneratedWorkItem.MaxTitleLength);
            if (title.Length == 0 || remaining <= 0 || !epicTitles.Add(title))
            {
                continue;
            }

            var features = (epic.Features ?? [])
                .Select(feature => new PlannedFeature(
                    Truncate(feature.Title?.Trim() ?? string.Empty, GeneratedWorkItem.MaxTitleLength),
                    Truncate(feature.Summary?.Trim() ?? string.Empty, MaxSummaryLength),
                    (feature.Paths ?? []).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Take(MaxPathsPerFeature).ToList()))
                .Where(feature => feature.Title.Length > 0 && featureTitles.Add(feature.Title))
                .Take(remaining)
                .ToList();
            if (features.Count == 0)
            {
                continue;
            }

            remaining -= features.Count;
            epics.Add(new PlannedEpic(title, Truncate(epic.Description?.Trim() ?? string.Empty, WorkItem.MaxDescriptionLength), features));
        }
        return epics;
    }

    /// <summary>Describes one planned feature and writes its user stories (and, for improvements, bugs).</summary>
    /// <param name="agent">The Repository Work Item Writer agent.</param>
    /// <param name="tools">Lets the agent read the repository's files.</param>
    /// <param name="workItems">Lets the agent read the description of a work item in the backlog.</param>
    public async Task<WrittenFeature> WriteFeatureWorkItemsAsync(InternalAgent agent, FeatureWritingInput input, RepositoryExplorerTools tools,
        WorkItemTools workItems, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            Repository = input.RepositoryFullName,
            Mode = ModeName(input.Mode),
            Epic = new { Title = input.EpicTitle, Description = Truncate(input.EpicDescription, MaxDescriptionLength) },
            Feature = new { input.Feature.Title, input.Feature.Summary, input.Feature.Paths },
            OtherFeatures = input.OtherFeatures,
            ExistingChildren = input.ExistingChildTitles,
            FeatureTemplate = Template(input.FeatureTemplate),
            UserStoryTemplate = Template(input.StoryTemplate),
            BugTemplate = input.Mode == RepositoryAnalysisMode.Improvements ? Template(input.BugTemplate) : null
        };
        var message = $"""
            Describe this feature and write its {(input.Mode == RepositoryAnalysisMode.Improvements ? "user stories and bugs" : "user stories")}.

            ```json
            {JsonSerializer.Serialize(request, JsonOptions)}
            ```

            {BacklogSection(input.Backlog)}

            The readable files of the repository, with their sizes:
            ```text
            {Truncate(input.Outline, MaxOutlineLength)}
            ```
            """;

        var response = await CreateAgent(agent, InternalAgentUsageSource.RepositoryWorkItemWriter, [.. tools.AsTools(), .. workItems.AsTools()])
            .RunAsync<WrittenFeatureOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        var titles = new HashSet<string>(input.ExistingChildTitles.Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);
        List<GeneratedWorkItem> NewItems(IEnumerable<(string? Title, string? Description)> items)
        {
            return items
                .Select(item => new GeneratedWorkItem(
                    Truncate(item.Title?.Trim() ?? string.Empty, GeneratedWorkItem.MaxTitleLength),
                    Truncate(item.Description?.Trim() ?? string.Empty, WorkItem.MaxDescriptionLength)))
                .Where(item => item.Title.Length > 0 && titles.Add(item.Title))
                .Take(GeneratedWorkItem.MaxPerRun)
                .ToList();
        }

        var output = response.Result;
        var stories = NewItems((output?.Stories ?? []).Select(story => (story.Title, story.Description)));
        var bugs = input.Mode == RepositoryAnalysisMode.Improvements
            ? NewItems((output?.Bugs ?? []).Select(bug => (bug.Title, bug.Description)))
            : [];
        return new WrittenFeature(Truncate(output?.FeatureDescription?.Trim() ?? string.Empty, WorkItem.MaxDescriptionLength), stories, bugs);
    }

    /// <summary>
    /// The backlog and how to use it. It is explained in the message rather than only in the system prompt, so agents
    /// whose prompt was stored before the backlog existed use it too.
    /// </summary>
    private static string BacklogSection(string backlog)
    {
        return $"""
            The whole backlog of the repository: every epic, feature, user story, bug and task with its status and id, children
            indented under their parent. Don't propose work that already exists in it (in any status), and don't overlap it.
            Items that are done or in progress describe what has been or is being implemented. Call `get_work_item` with an id to
            read an item's full description when you need it.
            ```text
            {backlog}
            ```
            """;
    }

    /// <summary>
    /// The mindmaps and parent work items of the repository, for the Chat Suggestions agent. Explained in the message
    /// rather than only in the system prompt, so agents whose prompt was stored before this context existed use it too.
    /// </summary>
    private static string RepositoryOverviewSection(RepositoryOverview repository)
    {
        return $"""

            The mindmaps of the repository: what is being thought about, as outlines of ideas. Done ideas are marked [done].
            ```text
            {Truncate(repository.Mindmaps, MaxMindmapsLength)}
            ```

            The epics, features and user stories of the repository with their status, children indented under their parent:
            the existing structure the item on the form may belong to or build on.
            ```text
            {Truncate(repository.ParentWorkItems, MaxParentWorkItemsLength)}
            ```

            Use both as inspiration and to make the suggestions concrete for this repository, e.g. an open mindmap idea that
            has no work item yet, or a gap under an existing epic or feature. Don't suggest work that already exists.
            """;
    }

    /// <summary>All mindmaps as indented outlines, one after the other; a note when there are none.</summary>
    public static string MindmapsOutline(IReadOnlyList<Mindmap> mindmaps, IReadOnlyDictionary<Guid, WorkItem> linkedItems)
    {
        if (mindmaps.Count == 0)
        {
            return "(The repository has no mindmaps.)";
        }

        return string.Join(Environment.NewLine + Environment.NewLine,
            mindmaps.Select(mindmap => MindmapOutline(mindmap, Guid.Empty, linkedItems)).Where(outline => outline.Length > 0));
    }

    private static string ModeName(RepositoryAnalysisMode mode)
    {
        return mode == RepositoryAnalysisMode.Improvements ? "improvements" : "describe-existing";
    }

    private static object? Template(WorkItemTemplate? template)
    {
        return template is null ? null : new { TitleExample = template.TitleTemplate, DescriptionSkeleton = Truncate(template.Description, MaxDescriptionLength) };
    }

    /// <summary>The mindmap as an indented outline, with the chosen idea marked and the done and linked ideas annotated.</summary>
    public static string MindmapOutline(Mindmap mindmap, Guid ideaId, IReadOnlyDictionary<Guid, WorkItem> linkedItems)
    {
        var builder = new System.Text.StringBuilder();
        void Write(MindmapNode node, int depth)
        {
            builder.Append(' ', depth * 2).Append("- ").Append(node.Text.ReplaceLineEndings(" "));
            if (node.IsDone)
            {
                builder.Append(" [done]");
            }
            if (node.WorkItemId is { } id && linkedItems.TryGetValue(id, out var item))
            {
                builder.Append(" [linked to ").Append(item.Type.DisplayName.ToLowerInvariant()).Append(": ").Append(item.Title).Append(']');
            }
            if (node.Id == ideaId)
            {
                builder.Append("  ⟵ write the work item for this idea");
            }
            builder.AppendLine();
            foreach (var child in mindmap.ChildrenOf(node.Id))
            {
                Write(child, depth + 1);
            }
        }

        if (mindmap.Root is { } root)
        {
            Write(root, 0);
        }
        return Truncate(builder.ToString().TrimEnd(), MaxDescriptionLength);
    }

    public const int ChatTitleWords = 2;
    private static readonly char[] TitlePunctuation = ['"', '\'', '`', '*', '.', ',', ';', ':', '!', '?', '“', '”', '‘', '’'];
    private const int MaxChatMessageLength = 4_000;

    /// <summary>Names a conversation from its first message; <c>null</c> when the model returns nothing usable.</summary>
    /// <param name="agent">The Chat Title Writer agent.</param>
    /// <param name="firstMessage">What the user wrote first.</param>
    public async Task<string?> WriteChatTitleAsync(InternalAgent agent, string firstMessage, CancellationToken cancellationToken = default)
    {
        var message = $"""
            The first message:
            ```text
            {Truncate(firstMessage.Trim(), MaxChatMessageLength)}
            ```
            """;
        var response = await CreateAgent(agent, InternalAgentUsageSource.ChatTitleWriter).RunAsync<ChatTitleOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        return CleanChatTitle(response.Result?.Title);
    }

    private const int MaxTranscriptLength = 60_000;

    /// <summary>
    /// Summarizes a conversation: the previous summary merged with the messages after it; <c>null</c> when the model
    /// returns nothing usable.
    /// </summary>
    /// <param name="agent">The Chat Summarizer agent.</param>
    /// <param name="previousSummary">The last summary of the conversation; <c>null</c> before the first one.</param>
    /// <param name="messages">The chat messages after it, in order.</param>
    public async Task<string?> SummarizeChatAsync(InternalAgent agent, string? previousSummary, IReadOnlyList<ChatDisplayMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var transcript = new System.Text.StringBuilder();
        foreach (var entry in messages)
        {
            transcript.Append(entry.Role == ChatMessageRole.User ? "### User\n" : "### DGHSF Agent\n");
            transcript.Append(Truncate(entry.Text.Trim(), MaxChatMessageLength)).Append("\n\n");
        }

        var message = $"""
            Summarize the conversation so far.

            ## Previous summary
            {(string.IsNullOrWhiteSpace(previousSummary) ? "(none: this is the first summary)" : previousSummary.Trim())}

            ## Messages after it
            {TruncateStart(transcript.ToString().TrimEnd(), MaxTranscriptLength)}
            """;
        var response = await CreateAgent(agent, InternalAgentUsageSource.ChatSummarizer)
            .RunAsync<ChatSummaryOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        var summary = response.Result?.Summary?.Trim();
        return string.IsNullOrEmpty(summary) ? null : Truncate(summary, ProjectChat.MaxSummaryLength);
    }

    /// <summary>The first <see cref="ChatTitleWords"/> words without quotes or trailing punctuation; <c>null</c> when none are left.</summary>
    public static string? CleanChatTitle(string? title)
    {
        var words = (title ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(TitlePunctuation))
            .Where(word => word.Length > 0)
            .Take(ChatTitleWords)
            .ToList();
        if (words.Count == 0)
        {
            return null;
        }

        var joined = string.Join(' ', words);
        joined = char.ToUpper(joined[0], System.Globalization.CultureInfo.CurrentCulture) + joined[1..];
        return Truncate(joined, 60);
    }

    private AIAgent CreateAgent(InternalAgent agent, InternalAgentUsageSource source, IList<AITool>? tools = null)
    {
        return factory.CreateAgent(agent.Name, agent.Instructions, tools, agent.Model, usage => usageRecorder.RecordAsync(agent, usage, source));
    }

    private static string Clean(string? suggestion)
    {
        var value = (suggestion ?? string.Empty).Trim().Trim('"', '\'').TrimEnd('.').Trim();
        return Truncate(value, MaxSuggestionLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
    }

    /// <summary>Keeps the end, the most recent part of a transcript.</summary>
    private static string TruncateStart(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat("…", value.AsSpan(value.Length - maxLength + 1));
    }

    [Description("The summary of the conversation so far.")]
    internal sealed record ChatSummaryOutput(
        [property: Description("The summary of the whole conversation as Markdown.")] string? Summary);

    [Description("The title of the conversation.")]
    internal sealed record ChatTitleOutput(
        [property: Description("Exactly two words.")] string? Title);

    [Description("The chosen model and the estimated complexity of the work.")]
    internal sealed record ModelSelectorOutput(
        [property: Description("The id of the chosen model, exactly as listed.")] string ModelId,
        [property: Description("The estimated complexity: low, medium or high.")] string? Complexity,
        [property: Description("One short sentence that explains the choice.")] string? Reason);

    [Description("The description with the dictated text merged in.")]
    internal sealed record DescriptionWriterOutput(
        [property: Description("The complete new description as Markdown.")] string? Description);

    [Description("The proposed child work items.")]
    internal sealed record ChildrenOutput(
        [property: Description("The child work items, in a sensible order of implementation.")] List<ChildOutput>? Items);

    [Description("One proposed child work item.")]
    internal sealed record ChildOutput(
        [property: Description("The title; no placeholders.")] string? Title,
        [property: Description("The description as Markdown.")] string? Description);

    [Description("The work items planned for the mindmap.")]
    internal sealed record MindmapPlanOutput(
        [property: Description("The work items, each parent before its children: first the epic of the central idea (unless it exists already).")]
        List<MindmapPlanItemOutput>? Items);

    [Description("One planned epic, feature or user story.")]
    internal sealed record MindmapPlanItemOutput(
        [property: Description("A unique number for this item: 1, 2, 3, ...")] int? Key,
        [property: Description("The key of the parent item in this plan; null for the epic and for items under an existing work item.")] int? Parent,
        [property: Description("For an item under an existing work item: the number of the idea linked to it; otherwise null.")] int? ExistingParentIdea,
        [property: Description("The type: Epic, Feature or User story.")] string? Type,
        [property: Description("The title; no placeholders.")] string? Title,
        [property: Description("The description as Markdown.")] string? Description,
        [property: Description("The numbers of the ideas this item is written for; it is linked to them.")] List<int>? Ideas);

    [Description("The planned epics with their features.")]
    internal sealed record RepositoryPlanOutput(
        [property: Description("The epics, most important first.")] List<EpicOutput>? Epics);

    [Description("One planned epic.")]
    internal sealed record EpicOutput(
        [property: Description("The title; the exact title of an existing epic to add features to it.")] string? Title,
        [property: Description("The epic's description as Markdown; ignored for an existing epic.")] string? Description,
        [property: Description("The new features of the epic.")] List<FeatureOutput>? Features);

    [Description("One planned feature.")]
    internal sealed record FeatureOutput(
        [property: Description("The title; no placeholders.")] string? Title,
        [property: Description("A few sentences on what the feature covers, as plain text.")] string? Summary,
        [property: Description("The files and folders the feature is about, relative to the repository root.")] List<string>? Paths);

    /// <remarks>
    /// Stories and bugs have their own types on purpose: a type used twice is written as a <c>$ref</c> to the first
    /// property, which OpenAI's strict structured output refuses (references may only point to top-level definitions).
    /// </remarks>
    [Description("The feature's description and its work items.")]
    internal sealed record WrittenFeatureOutput(
        [property: Description("The complete description of the feature as Markdown.")] string? FeatureDescription,
        [property: Description("The user stories of the feature, in a sensible order.")] List<StoryOutput>? Stories,
        [property: Description("Concrete defects found in the code (improvements mode only); otherwise empty.")] List<BugOutput>? Bugs);

    [Description("One user story.")]
    internal sealed record StoryOutput(
        [property: Description("The title; no placeholders.")] string? Title,
        [property: Description("The description as Markdown, with acceptance criteria.")] string? Description);

    [Description("One bug: a concrete defect found in the code.")]
    internal sealed record BugOutput(
        [property: Description("The title; no placeholders.")] string? Title,
        [property: Description("The description as Markdown: what goes wrong, where, and what is expected.")] string? Description);

    [Description("Suggestion chips for an agent chat.")]
    internal sealed record SuggestionsOutput(
        [property: Description("Exactly 3 messages the user could send to the chat agent.")] List<string>? Suggestions);
}

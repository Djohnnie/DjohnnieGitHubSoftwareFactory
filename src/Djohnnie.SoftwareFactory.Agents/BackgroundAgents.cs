using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.Speech;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Microsoft.Agents.AI;

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

    /// <summary>Writes example prompts for a Copilot; empty when the agent returns nothing usable.</summary>
    /// <param name="agent">The Chat Suggestions agent.</param>
    /// <param name="copilot">The Copilot the suggestions are for.</param>
    /// <param name="context">Extra page context, e.g. the selected repository.</param>
    /// <param name="tools">The Copilot's tools; their form snapshot is sent along.</param>
    public async Task<IReadOnlyList<string>> SuggestPromptsAsync(InternalAgent agent, InternalAgent copilot, string? context, AssistantTools? tools,
        CancellationToken cancellationToken = default)
    {
        var form = tools?.Snapshot is { } snapshot ? await snapshot() : null;
        var formJson = form is null ? "null" : Truncate(JsonSerializer.Serialize(form, form.GetType(), JsonOptions), MaxFormLength);
        var message = $"""
            Copilot: {copilot.Name}
            Purpose: {copilot.Description}
            Context: {(string.IsNullOrWhiteSpace(context) ? "(none)" : context.Trim())}

            Current form:
            ```json
            {formJson}
            ```
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

    private AIAgent CreateAgent(InternalAgent agent, InternalAgentUsageSource source)
    {
        return factory.CreateAgent(agent.Name, agent.Instructions, model: agent.Model, onUsage: usage => usageRecorder.RecordAsync(agent, usage, source));
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

    [Description("Suggestion chips for a Copilot chat.")]
    internal sealed record SuggestionsOutput(
        [property: Description("Exactly 3 messages the user could send to the Copilot.")] List<string>? Suggestions);
}

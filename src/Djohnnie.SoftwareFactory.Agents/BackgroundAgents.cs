using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.Agents.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Runs the internal agents that work without a chat. They use structured output, so the result format doesn't depend
/// on the (editable) system prompt.
/// </summary>
public sealed class BackgroundAgents(AssistantAgentFactory factory)
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

        var response = await factory.CreateAgent(agent.Name, agent.Instructions)
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

        var response = await factory.CreateAgent(agent.Name, agent.Instructions)
            .RunAsync<SuggestionsOutput>(message, serializerOptions: JsonOptions, cancellationToken: cancellationToken);
        return (response.Result?.Suggestions ?? [])
            .Select(Clean)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(SuggestionCount)
            .ToList();
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

    [Description("Suggestion chips for a Copilot chat.")]
    internal sealed record SuggestionsOutput(
        [property: Description("Exactly 3 messages the user could send to the Copilot.")] List<string>? Suggestions);
}

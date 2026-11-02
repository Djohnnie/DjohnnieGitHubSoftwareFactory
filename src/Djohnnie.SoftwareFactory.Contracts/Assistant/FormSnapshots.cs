using System.Text.Json.Serialization;

namespace Djohnnie.SoftwareFactory.Contracts.Assistant;

public sealed record AgentFormSnapshot(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("toolsMode")] string ToolsMode,
    [property: JsonPropertyName("tools")] IReadOnlyList<string> Tools,
    [property: JsonPropertyName("argumentHint")] string? ArgumentHint,
    [property: JsonPropertyName("userInvocable")] bool UserInvocable,
    [property: JsonPropertyName("disableModelInvocation")] bool DisableModelInvocation);

public sealed record EpicFormSnapshot(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("customAgent")] string CustomAgent,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("baseBranch")] string BaseBranch,
    [property: JsonPropertyName("customInstructions")] string? CustomInstructions);

public sealed record ModelChoice(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("inputPricePer1MTokens")] decimal? InputPricePer1MTokens,
    [property: JsonPropertyName("outputPricePer1MTokens")] decimal? OutputPricePer1MTokens);

public sealed record CustomAgentChoice(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description);

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

/// <remarks>The assignment fields are only filled in for types that are assigned to an agent.</remarks>
public sealed record WorkItemFormSnapshot(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("parent")] string? Parent,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("technicalDetails")] string TechnicalDetails,
    [property: JsonPropertyName("customAgent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CustomAgent,
    [property: JsonPropertyName("model"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Model,
    [property: JsonPropertyName("baseBranch"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BaseBranch,
    [property: JsonPropertyName("customInstructions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CustomInstructions);

/// <remarks>One parent of a work item; <c>technicalDetails</c> is empty when the parent has none.</remarks>
public sealed record ParentTechnicalDetailsSnapshot(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("technicalDetails")] string TechnicalDetails);

/// <remarks><c>selected</c> is <c>false</c> when the form has no template; the other fields are then left out.</remarks>
public sealed record SelectedWorkItemTemplateSnapshot(
    [property: JsonPropertyName("selected")] bool Selected,
    [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    [property: JsonPropertyName("titleExample"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TitleExample,
    [property: JsonPropertyName("descriptionSkeleton"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DescriptionSkeleton);

public sealed record ModelChoice(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("inputAiCreditsPer1MTokens")] decimal? InputAiCreditsPer1MTokens,
    [property: JsonPropertyName("outputAiCreditsPer1MTokens")] decimal? OutputAiCreditsPer1MTokens);

public sealed record CustomAgentChoice(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description);

public sealed record WorkItemTemplateFormSnapshot(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("titleExample")] string TitleExample,
    [property: JsonPropertyName("descriptionSkeleton")] string DescriptionSkeleton);

/// <remarks><c>exists</c> is <c>false</c> while the file hasn't been committed to the repository yet.</remarks>
public sealed record CopilotInstructionsFormSnapshot(
    [property: JsonPropertyName("repository")] string Repository,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("exists")] bool Exists,
    [property: JsonPropertyName("content")] string Content);

public sealed record RepositoryEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("type")] string Type);

/// <remarks>The optional properties are <c>null</c> when they are left out of the frontmatter.</remarks>
public sealed record SkillFormSnapshot(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("license")] string? License,
    [property: JsonPropertyName("compatibility")] string? Compatibility,
    [property: JsonPropertyName("allowedTools")] string? AllowedTools);

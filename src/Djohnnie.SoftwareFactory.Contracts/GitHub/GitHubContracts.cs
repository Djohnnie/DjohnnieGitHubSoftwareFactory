using System.Text.Json.Serialization;

namespace Djohnnie.SoftwareFactory.Contracts.GitHub;

public sealed record GitHubOwner(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

public sealed record GitHubRepository(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("owner")] GitHubOwner Owner,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("private")] bool Private,
    [property: JsonPropertyName("fork")] bool Fork,
    [property: JsonPropertyName("archived")] bool Archived,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("default_branch")] string? DefaultBranch,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt);

public sealed record GitHubContentItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("html_url")] string? HtmlUrl);

public sealed record GitHubFile(string Path, string Sha, string Content, string? HtmlUrl);

public sealed record GitHubCommitResult(string? ContentSha, string CommitSha, string? CommitUrl);

public sealed record GitHubIssue(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("assignees")] IReadOnlyList<GitHubOwner>? Assignees);

/// <summary>Options for a Copilot cloud agent assignment (the <c>agent_assignment</c> object of the issues API).</summary>
public sealed record CopilotAssignment(
    [property: JsonPropertyName("target_repo")] string TargetRepository,
    [property: JsonPropertyName("base_branch"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BaseBranch,
    [property: JsonPropertyName("custom_instructions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CustomInstructions,
    [property: JsonPropertyName("custom_agent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CustomAgent,
    [property: JsonPropertyName("model"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Model);

/// <param name="Configured">The level stored for the repository; <c>null</c> when it inherits one.</param>
/// <param name="Effective">The level reviews use when the requester and the pull request don't choose one.</param>
public sealed record CopilotCodeReviewEffort(
    [property: JsonPropertyName("configured")] string? Configured,
    [property: JsonPropertyName("effective")] string Effective);

/// <summary>A repository's Copilot code review settings. They tune reviews; they don't enable Copilot code review.</summary>
public sealed record CopilotCodeReviewSettings(
    [property: JsonPropertyName("review_effort")] CopilotCodeReviewEffort ReviewEffort,
    [property: JsonPropertyName("available_review_efforts")] IReadOnlyList<string> AvailableReviewEfforts,
    [property: JsonPropertyName("severity_filter")] string SeverityFilter,
    [property: JsonPropertyName("custom_instructions_enabled")] bool CustomInstructionsEnabled,
    [property: JsonPropertyName("mcp_enabled")] bool McpEnabled)
{
    /// <summary>The review effort value that clears the repository's own level, so it inherits one again.</summary>
    public const string InheritReviewEffort = "inherit";
}

/// <summary>The Copilot code review settings to change; GitHub leaves settings that are <c>null</c> here as they are.</summary>
public sealed record CopilotCodeReviewSettingsUpdate(
    [property: JsonPropertyName("review_effort"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReviewEffort = null,
    [property: JsonPropertyName("severity_filter"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SeverityFilter = null,
    [property: JsonPropertyName("custom_instructions_enabled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CustomInstructionsEnabled = null,
    [property: JsonPropertyName("mcp_enabled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? McpEnabled = null);


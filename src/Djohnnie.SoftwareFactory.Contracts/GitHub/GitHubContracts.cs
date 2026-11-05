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


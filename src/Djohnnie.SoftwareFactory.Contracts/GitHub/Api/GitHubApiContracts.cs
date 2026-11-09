using System.Text.Json.Serialization;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Contracts.GitHub.Api;

public sealed record FileResponse(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("encoding")] string? Encoding = "base64");

public sealed record PutFileRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("sha"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sha);

public sealed record DeleteFileRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("sha")] string Sha);

public sealed record CommitResponse(
    [property: JsonPropertyName("content")] ShaResponse? Content,
    [property: JsonPropertyName("commit")] CommitInfo? Commit);

public sealed record ShaResponse([property: JsonPropertyName("sha")] string Sha);

public sealed record CommitInfo(
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("html_url")] string? HtmlUrl);

public sealed record ErrorResponse([property: JsonPropertyName("message")] string? Message);

public sealed record LabelRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description);

public sealed record CreateIssueRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("labels")] IReadOnlyList<string> Labels);

public sealed record AssignRequest(
    [property: JsonPropertyName("assignees")] IReadOnlyList<string> Assignees,
    [property: JsonPropertyName("agent_assignment")] CopilotAssignment AgentAssignment);

public sealed record AddSubIssueRequest([property: JsonPropertyName("sub_issue_id")] long SubIssueId);

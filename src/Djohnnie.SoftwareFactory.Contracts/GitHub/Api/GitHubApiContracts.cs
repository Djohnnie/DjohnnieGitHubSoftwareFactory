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

/// <summary>A Copilot usage metrics report: signed links to its NDJSON files.</summary>
public sealed record MetricsReportResponse(
    [property: JsonPropertyName("download_links")] IReadOnlyList<string>? DownloadLinks);

/// <summary>One line of a <c>repos-1-day</c> report: one repository with pull request activity that day.</summary>
public sealed record RepositoryMetricsRow(
    [property: JsonPropertyName("day")] DateOnly Day,
    [property: JsonPropertyName("repo_owner_name")] string? OwnerName,
    [property: JsonPropertyName("repo_name")] string? Name,
    [property: JsonPropertyName("pull_requests")] PullRequestMetrics? PullRequests,
    [property: JsonPropertyName("copilot_code_review")] CodeReviewMetrics? CodeReview);

public sealed record PullRequestMetrics(
    [property: JsonPropertyName("total_created")] int TotalCreated,
    [property: JsonPropertyName("total_created_by_copilot")] int TotalCreatedByCopilot,
    [property: JsonPropertyName("total_merged")] int TotalMerged,
    [property: JsonPropertyName("total_merged_created_by_copilot")] int TotalMergedCreatedByCopilot,
    [property: JsonPropertyName("total_reviewed_by_copilot")] int TotalReviewedByCopilot);

public sealed record CodeReviewMetrics(
    [property: JsonPropertyName("total_comments_evaluated")] int TotalCommentsEvaluated,
    [property: JsonPropertyName("total_comments_addressed")] int TotalCommentsAddressed,
    [property: JsonPropertyName("review_comments_by_severity")] IReadOnlyList<SeverityMetrics>? BySeverity);

public sealed record SeverityMetrics(
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("total_comments_evaluated")] int TotalCommentsEvaluated,
    [property: JsonPropertyName("total_comments_addressed")] int TotalCommentsAddressed);

/// <summary>A premium request or AI credit usage report of the billing API.</summary>
public sealed record BillingUsageResponse(
    [property: JsonPropertyName("usageItems")] IReadOnlyList<CopilotBillingUsageItem>? UsageItems);

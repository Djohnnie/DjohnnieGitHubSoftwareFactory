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

/// <param name="Errors">
/// The reasons behind a 422: GitHub sends plain strings (e.g. "Can not approve your own pull request") or objects with
/// <c>message</c>, <c>code</c> and <c>field</c>.
/// </param>
public sealed record ErrorResponse(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("errors")] System.Text.Json.JsonElement? Errors = null);

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

public sealed record CreatePullRequestRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("head")] string Head,
    [property: JsonPropertyName("base")] string Base,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("draft")] bool Draft);

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

public sealed record WorkflowRunsResponse(
    [property: JsonPropertyName("workflow_runs")] IReadOnlyList<GitHubWorkflowRun>? WorkflowRuns,
    [property: JsonPropertyName("total_count")] int TotalCount = 0);

public sealed record WorkflowsResponse(
    [property: JsonPropertyName("workflows")] IReadOnlyList<GitHubWorkflow>? Workflows);

public sealed record GraphQlRequest(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("variables")] object Variables);

public sealed record GraphQlResponse<T>(
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("errors")] IReadOnlyList<GraphQlError>? Errors);

/// <param name="Type">E.g. <c>NOT_FOUND</c>, <c>FORBIDDEN</c> or <c>RATE_LIMITED</c>.</param>
public sealed record GraphQlError(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("type")] string? Type);

public sealed record GraphQlNodes<T>([property: JsonPropertyName("nodes")] IReadOnlyList<T?>? Nodes);

public sealed record LinkedPullRequestsData([property: JsonPropertyName("repository")] LinkedPullRequestsRepository? Repository);

public sealed record LinkedPullRequestsRepository([property: JsonPropertyName("issue")] LinkedPullRequestsIssue? Issue);

public sealed record LinkedPullRequestsIssue(
    [property: JsonPropertyName("closedByPullRequestsReferences")] GraphQlNodes<LinkedPullRequestNode>? PullRequests);

public sealed record LinkedPullRequestNode(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("isDraft")] bool IsDraft,
    [property: JsonPropertyName("headRefName")] string HeadRefName,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("reviewDecision")] string? ReviewDecision,
    [property: JsonPropertyName("latestReviews")] GraphQlNodes<ReviewStateNode>? LatestReviews,
    [property: JsonPropertyName("commits")] GraphQlNodes<PullRequestCommitNode>? Commits,
    [property: JsonPropertyName("reviewRequests")] GraphQlNodes<ReviewRequestNode>? ReviewRequests = null,
    [property: JsonPropertyName("mergeable")] string? Mergeable = null);

public sealed record ReviewStateNode([property: JsonPropertyName("state")] string? State);

/// <summary>A pending review request; GitHub removes it when the reviewer submits their review.</summary>
public sealed record ReviewRequestNode([property: JsonPropertyName("requestedReviewer")] RequestedReviewerNode? RequestedReviewer);

/// <param name="TypeName">GraphQL's <c>__typename</c>: <c>User</c>, <c>Bot</c>, <c>Team</c> or <c>Mannequin</c>.</param>
/// <param name="Login">A user's or bot's login; <c>null</c> for a team.</param>
public sealed record RequestedReviewerNode(
    [property: JsonPropertyName("__typename")] string? TypeName,
    [property: JsonPropertyName("login")] string? Login);

public sealed record PullRequestCommitNode([property: JsonPropertyName("commit")] CommitStatusNode? Commit);

public sealed record CommitStatusNode([property: JsonPropertyName("statusCheckRollup")] StatusCheckRollupNode? StatusCheckRollup);

public sealed record StatusCheckRollupNode([property: JsonPropertyName("state")] string? State);

public sealed record CreateCommentRequest([property: JsonPropertyName("body")] string Body);

public sealed record RequestReviewersRequest([property: JsonPropertyName("reviewers")] IReadOnlyList<string> Reviewers);

/// <param name="Event"><c>COMMENT</c> or <c>REQUEST_CHANGES</c>.</param>
public sealed record CreateReviewRequest(
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("comments")] IReadOnlyList<GitHubReviewLineComment> Comments);

/// <param name="MergeMethod"><c>merge</c>, <c>squash</c> or <c>rebase</c>.</param>
public sealed record MergePullRequestRequest([property: JsonPropertyName("merge_method")] string MergeMethod);

public sealed record MergePullRequestResponse(
    [property: JsonPropertyName("sha")] string? Sha,
    [property: JsonPropertyName("merged")] bool Merged,
    [property: JsonPropertyName("message")] string? Message);

public sealed record HtmlUrlResponse([property: JsonPropertyName("html_url")] string HtmlUrl);

public sealed record TreeResponse(
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("tree")] List<TreeEntryResponse>? Tree);

public sealed record TreeEntryResponse(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("size")] long? Size);

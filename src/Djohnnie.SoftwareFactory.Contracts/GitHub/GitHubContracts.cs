using System.Text.Json.Serialization;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts.GitHub;

/// <param name="Type">"User" or "Organization"; <c>null</c> in copies stored before it was read.</param>
public sealed record GitHubOwner(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("type")] string? Type = null)
{
    [JsonIgnore]
    public bool IsOrganization => string.Equals(Type, "Organization", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Copilot cloud agent (author "Copilot") or Copilot code review (reviewer "copilot-pull-request-reviewer[bot]").</summary>
    [JsonIgnore]
    public bool IsCopilot => string.Equals(Type, "Bot", StringComparison.OrdinalIgnoreCase)
        && Login.Contains("copilot", StringComparison.OrdinalIgnoreCase);
}

public sealed record GitHubPullRequest(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("user")] GitHubOwner? User,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("merged_at")] DateTimeOffset? MergedAt,
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("html_url")] string? HtmlUrl = null,
    [property: JsonPropertyName("draft")] bool Draft = false,
    [property: JsonPropertyName("head")] GitHubBranchRef? Head = null);

/// <summary>A pull request's head or base: the branch it comes from or goes into.</summary>
public sealed record GitHubBranchRef([property: JsonPropertyName("ref")] string Ref);

/// <param name="State"><c>APPROVED</c>, <c>CHANGES_REQUESTED</c>, <c>COMMENTED</c>, <c>DISMISSED</c> or <c>PENDING</c>.</param>
public sealed record GitHubPullRequestReview(
    [property: JsonPropertyName("user")] GitHubOwner? User,
    [property: JsonPropertyName("submitted_at")] DateTimeOffset? SubmittedAt,
    [property: JsonPropertyName("state")] string? State = null,
    [property: JsonPropertyName("html_url")] string? HtmlUrl = null,
    [property: JsonPropertyName("id")] long Id = 0,
    [property: JsonPropertyName("body")] string? Body = null);

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
    [property: JsonPropertyName("assignees")] IReadOnlyList<GitHubOwner>? Assignees,
    [property: JsonPropertyName("body")] string? Body = null)
{
    public bool IsClosed => string.Equals(State, "closed", StringComparison.OrdinalIgnoreCase);
}

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


/// <summary>A pull request linked to an issue (one that closes it when merged), as GraphQL's <c>closedByPullRequestsReferences</c> returns it.</summary>
/// <param name="State">GraphQL's <c>OPEN</c>, <c>CLOSED</c> or <c>MERGED</c>.</param>
/// <param name="ReviewDecision">GraphQL's <c>APPROVED</c>, <c>CHANGES_REQUESTED</c> or <c>REVIEW_REQUIRED</c>; <c>null</c> when no review is required.</param>
/// <param name="LatestReviewStates">The latest review state of each reviewer, e.g. <c>APPROVED</c> or <c>CHANGES_REQUESTED</c>.</param>
/// <param name="ChecksState">The status check rollup of the last commit (<c>SUCCESS</c>, <c>FAILURE</c>, <c>ERROR</c>, <c>PENDING</c> or <c>EXPECTED</c>), or <c>null</c> without checks.</param>
/// <param name="CopilotReviewRequested">Copilot code review was asked to review and hasn't submitted its review yet.</param>
public sealed record GitHubLinkedPullRequest(
    int Number,
    string Url,
    string Title,
    string State,
    bool IsDraft,
    string HeadRefName,
    DateTimeOffset UpdatedAt,
    string? ReviewDecision,
    IReadOnlyList<string> LatestReviewStates,
    string? ChecksState,
    bool CopilotReviewRequested = false)
{
    public bool IsOpen => State == "OPEN";
    public bool IsMerged => State == "MERGED";
}

/// <summary>A GitHub Actions workflow run.</summary>
/// <param name="Event">What triggered it; Copilot's agent sessions are <c>dynamic</c>.</param>
/// <param name="Status"><c>queued</c>, <c>in_progress</c>, <c>completed</c>, …</param>
/// <param name="Conclusion">For a completed run: <c>success</c>, <c>failure</c>, <c>cancelled</c>, <c>timed_out</c>, …</param>
public sealed record GitHubWorkflowRun(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("event")] string? Event,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("head_branch")] string? HeadBranch = null,
    [property: JsonPropertyName("run_started_at")] DateTimeOffset? RunStartedAt = null,
    [property: JsonPropertyName("display_title")] string? DisplayTitle = null,
    [property: JsonPropertyName("run_number")] int RunNumber = 0,
    [property: JsonPropertyName("run_attempt")] int RunAttempt = 1,
    [property: JsonPropertyName("workflow_id")] long WorkflowId = 0,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt = null,
    [property: JsonPropertyName("actor")] GitHubOwner? Actor = null)
{
    [JsonIgnore]
    public bool IsCompleted => string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How long a completed run took: from the start of its latest attempt to its last update (GitHub's runs list has no
    /// completion time; a completed run isn't updated anymore). <c>null</c> while it runs.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration => IsCompleted && UpdatedAt is { } updated && updated >= (RunStartedAt ?? CreatedAt)
        ? updated - (RunStartedAt ?? CreatedAt)
        : null;
}

/// <summary>A GitHub Actions workflow of a repository, as <c>GET /repos/{owner}/{repo}/actions/workflows</c> lists it.</summary>
/// <param name="Path">The workflow file, e.g. <c>.github/workflows/release.yml</c>; GitHub's own workflows (such as Copilot's agent) have a <c>dynamic/</c> path.</param>
/// <param name="State"><c>active</c>, <c>disabled_manually</c>, <c>disabled_inactivity</c>, …</param>
public sealed record GitHubWorkflow(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("html_url")] string? HtmlUrl)
{
    [JsonIgnore]
    public bool IsActive => string.Equals(State, "active", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Copilot's latest agent run on a branch, as far as the token may see it.</summary>
/// <param name="Run"><c>null</c> when there is none, or when <paramref name="CanReadActions"/> is false.</param>
/// <param name="CanReadActions">False when GitHub refused to list the runs (a token without Actions access, or Actions disabled).</param>
public sealed record CopilotRunLookup(GitHubWorkflowRun? Run, bool CanReadActions);

/// <summary>One pull request as <c>GET /repos/{owner}/{repo}/pulls/{number}</c> returns it.</summary>
/// <param name="State"><c>open</c> or <c>closed</c>; a merged pull request is closed with <see cref="MergedAt"/> set.</param>
public sealed record GitHubPullRequestDetails(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("user")] GitHubOwner? User,
    [property: JsonPropertyName("merged_at")] DateTimeOffset? MergedAt,
    [property: JsonPropertyName("additions")] int Additions,
    [property: JsonPropertyName("deletions")] int Deletions,
    [property: JsonPropertyName("changed_files")] int ChangedFiles,
    [property: JsonPropertyName("mergeable_state")] string? MergeableState = null,
    [property: JsonPropertyName("head")] GitHubPullRequestBranch? Head = null,
    [property: JsonPropertyName("base")] GitHubPullRequestBranch? Base = null,
    [property: JsonPropertyName("body")] string? Body = null,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt = default,
    [property: JsonPropertyName("requested_reviewers")] IReadOnlyList<GitHubOwner>? RequestedReviewers = null)
{
    public bool IsOpen => string.Equals(State, "open", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The head or base of a pull request: the branch and the commit it points at.</summary>
public sealed record GitHubPullRequestBranch(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("sha")] string? Sha = null);

/// <summary>A file of a pull request's diff.</summary>
/// <param name="Patch">Missing for binary files and diffs that are too large.</param>
public sealed record GitHubPullRequestFile(
    [property: JsonPropertyName("filename")] string FileName,
    [property: JsonPropertyName("previous_filename")] string? PreviousFileName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("additions")] int Additions,
    [property: JsonPropertyName("deletions")] int Deletions,
    [property: JsonPropertyName("patch")] string? Patch,
    [property: JsonPropertyName("blob_url")] string? BlobUrl);

/// <summary>An issue (conversation) comment, or a review comment on a line when <see cref="Path"/> is set.</summary>
/// <param name="Line">For a review comment: the line in the file it is on; <c>null</c> when that line is no longer in the diff.</param>
/// <param name="DiffHunk">For a review comment: the diff as it was when it was written, ending at its (last) line.</param>
/// <param name="StartLine">For a review comment on several lines: the first of them.</param>
public sealed record GitHubComment(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("user")] GitHubOwner? User,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("line")] int? Line = null,
    [property: JsonPropertyName("original_line")] int? OriginalLine = null,
    [property: JsonPropertyName("in_reply_to_id")] long? InReplyToId = null,
    [property: JsonPropertyName("diff_hunk")] string? DiffHunk = null,
    [property: JsonPropertyName("start_line")] int? StartLine = null,
    [property: JsonPropertyName("original_start_line")] int? OriginalStartLine = null,
    [property: JsonPropertyName("pull_request_review_id")] long? PullRequestReviewId = null);

/// <summary>A line comment of a review that is being submitted.</summary>
/// <param name="Side"><c>RIGHT</c> (new file) or <c>LEFT</c> (old file).</param>
public sealed record GitHubReviewLineComment(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("body")] string Body);

/// <summary>
/// The open pull requests of a repository and the agent sessions running in it right now: the Copilot cloud agent's
/// Actions runs and the DGHSF agent worker's active tasks.
/// </summary>
/// <param name="SessionsUnavailable">The token may not read Actions, so the running Copilot sessions are unknown (not zero).</param>
/// <param name="OtherSessions">Running Copilot sessions whose branch has no open pull request (yet).</param>
/// <param name="OtherWorkerSessions">Active DGHSF agent worker tasks without an open pull request (yet).</param>
public sealed record CopilotPullRequestOverview(
    IReadOnlyList<OpenPullRequest> OpenPullRequests,
    IReadOnlyList<CopilotSession> OtherSessions,
    IReadOnlyList<AgentWorkerSession> OtherWorkerSessions,
    bool SessionsUnavailable,
    DateTimeOffset CheckedAt)
{
    public int ByCopilot => OpenPullRequests.Count(p => p.Agent == AgentExecutor.CopilotCloudAgent);
    public int ByWorker => OpenPullRequests.Count(p => p.Agent == AgentExecutor.DghsfWorker);
    public int Drafts => OpenPullRequests.Count(p => p.IsDraft);
    public int RunningSessions => OpenPullRequests.Count(p => p.Session is { IsQueued: false }) + OtherSessions.Count(s => !s.IsQueued);
    public int QueuedSessions => OpenPullRequests.Count(p => p.Session is { IsQueued: true }) + OtherSessions.Count(s => s.IsQueued);
    public int RunningWorkerSessions => OpenPullRequests.Count(p => p.WorkerSession is { IsQueued: false }) + OtherWorkerSessions.Count(s => !s.IsQueued);
    public int QueuedWorkerSessions => OpenPullRequests.Count(p => p.WorkerSession is { IsQueued: true }) + OtherWorkerSessions.Count(s => s.IsQueued);
}

/// <param name="Agent">The agent that works on the pull request, or <c>null</c> when no agent does (e.g. a person opened it).</param>
/// <param name="Session">Copilot's session on the pull request's branch, when one is running or queued.</param>
/// <param name="WorkerSession">The DGHSF agent worker's active task on the pull request, when one is running or queued.</param>
/// <param name="WorkItem">The DGHSF work item whose progress is about this pull request, if any.</param>
public sealed record OpenPullRequest(
    int Number,
    string Title,
    string Url,
    string? AuthorLogin,
    bool ByCopilot,
    bool IsDraft,
    string? Branch,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AgentExecutor? Agent,
    CopilotSession? Session,
    AgentWorkerSession? WorkerSession,
    OpenPullRequestWorkItem? WorkItem);

/// <summary>A work item linked to an open pull request (a short reference, to link to it), with how far its agent got.</summary>
public sealed record OpenPullRequestWorkItem(
    Guid Id,
    WorkItemType Type,
    string Title,
    CopilotProgress Progress = CopilotProgress.None,
    PullRequestChecks Checks = PullRequestChecks.None);

/// <summary>A Copilot cloud agent session: the Actions run it works in.</summary>
/// <param name="StartedAt">When it started running, or when it was queued.</param>
public sealed record CopilotSession(long RunId, string Url, bool IsQueued, DateTimeOffset StartedAt, string? Branch);

/// <summary>An active task of the DGHSF agent worker: waiting, queued or running.</summary>
/// <param name="StartedAt">When its current run started, or when it was created or queued.</param>
/// <param name="WorkItem">The work item the task implements; <c>null</c> when it was deleted.</param>
public sealed record AgentWorkerSession(Guid RunId, bool IsQueued, DateTimeOffset StartedAt, string? Branch, OpenPullRequestWorkItem? WorkItem);

/// <summary>A workflow with its most recent run.</summary>
/// <param name="LatestRun"><c>null</c> when the workflow never ran.</param>
public sealed record LatestWorkflowRun(GitHubWorkflow Workflow, GitHubWorkflowRun? LatestRun);

/// <summary>The full file tree of a commit, read with one request.</summary>
/// <param name="Truncated">GitHub left entries out because the tree is too large (over 100,000 entries or 7 MB).</param>
public sealed record GitHubTree(string Sha, bool Truncated, IReadOnlyList<GitHubTreeEntry> Entries);

/// <param name="Type"><c>blob</c> for a file, <c>tree</c> for a folder, <c>commit</c> for a submodule.</param>
/// <param name="Size">The size in bytes of a file; <c>null</c> for other entries.</param>
public sealed record GitHubTreeEntry(string Path, string Type, long? Size)
{
    public bool IsFile
    {
        get
        {
            return Type == "blob";
        }
    }
}

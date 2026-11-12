using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api = Djohnnie.SoftwareFactory.Contracts.GitHub.Api;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GitHubApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed partial class GitHubApiClient(HttpClient httpClient, IGitHubTokenProvider tokenProvider)
{
    private const int PageSize = 100;

    public async Task<GitHubRepository> GetRepositoryAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubRepository>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>Lists all repositories the signed-in user can access, most recently pushed first.</summary>
    public async Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        var repositories = new List<GitHubRepository>();
        string? url = $"user/repos?sort=pushed&direction=desc&per_page={PageSize}&affiliation=owner,collaborator,organization_member";

        while (url is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<List<GitHubRepository>>(cancellationToken) ?? [];
            repositories.AddRange(page);
            url = GetNextLink(response);
        }

        return repositories
            .OrderByDescending(r => r.PushedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>Lists the files in a repository directory, or an empty list when the directory does not exist.</summary>
    public async Task<IReadOnlyList<GitHubContentItem>> GetDirectoryAsync(string repositoryFullName, string path, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, ContentsUrl(repositoryFullName, path), null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<GitHubContentItem>>(cancellationToken) ?? [];
    }

    /// <summary>Gets a file from the default branch, or <c>null</c> when it does not exist.</summary>
    /// <exception cref="GitHubApiException">The path is a directory, symlink or submodule, or GitHub refused the request.</exception>
    public async Task<GitHubFile?> GetFileAsync(string repositoryFullName, string path, CancellationToken cancellationToken = default)
    {
        var url = ContentsUrl(repositoryFullName, path);
        using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            // The contents API lists a directory instead of returning a file.
            throw new GitHubApiException(HttpStatusCode.UnprocessableEntity, $"'{path}' is a directory, not a file.");
        }

        var file = document.RootElement.Deserialize<Api.FileResponse>()
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
        if (file.Type is { } type && type != "file")
        {
            throw new GitHubApiException(HttpStatusCode.UnprocessableEntity, $"'{path}' is a {type}, not a file.");
        }

        var content = file.Encoding == "base64" && file.Content is not null
            ? Encoding.UTF8.GetString(Convert.FromBase64String(file.Content.Replace("\n", string.Empty)))
            // GitHub leaves out the content of files over 1 MB (encoding "none"); the raw media type returns up to 100 MB.
            : await GetRawContentAsync(url, cancellationToken);
        return new GitHubFile(file.Path, file.Sha, content, file.HtmlUrl);
    }

    private async Task<string> GetRawContentAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken, RawMediaType);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private const string RawMediaType = "application/vnd.github.raw+json";

    /// <summary>
    /// Creates or updates a file with a single commit on the default branch.
    /// Pass the current blob <paramref name="sha"/> to update; leave it <c>null</c> to create.
    /// </summary>
    public async Task<GitHubCommitResult> PutFileAsync(string repositoryFullName, string path, string content, string message, string? sha, CancellationToken cancellationToken = default)
    {
        var body = new Api.PutFileRequest(message, Convert.ToBase64String(Encoding.UTF8.GetBytes(content)), sha);
        using var response = await SendAsync(HttpMethod.Put, ContentsUrl(repositoryFullName, path), JsonContent.Create(body), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<Api.CommitResponse>(cancellationToken);
        return new GitHubCommitResult(result?.Content?.Sha, result?.Commit?.Sha ?? string.Empty, result?.Commit?.HtmlUrl);
    }

    /// <summary>Deletes a file with a single commit on the default branch.</summary>
    public async Task<GitHubCommitResult> DeleteFileAsync(string repositoryFullName, string path, string message, string sha, CancellationToken cancellationToken = default)
    {
        var body = new Api.DeleteFileRequest(message, sha);
        using var response = await SendAsync(HttpMethod.Delete, ContentsUrl(repositoryFullName, path), JsonContent.Create(body), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<Api.CommitResponse>(cancellationToken);
        return new GitHubCommitResult(null, result?.Commit?.Sha ?? string.Empty, result?.Commit?.HtmlUrl);
    }

    /// <summary>Creates the label when the repository doesn't have it yet.</summary>
    public async Task EnsureLabelAsync(string repositoryFullName, string name, string color, string? description, CancellationToken cancellationToken = default)
    {
        using (var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}/labels/{Uri.EscapeDataString(name)}", null, cancellationToken))
        {
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(response, cancellationToken);
                return;
            }
        }

        using var create = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/labels",
            JsonContent.Create(new Api.LabelRequest(name, color, description)), cancellationToken);
        // 422: created concurrently in the meantime.
        if (create.StatusCode != HttpStatusCode.UnprocessableEntity)
        {
            await EnsureSuccessAsync(create, cancellationToken);
        }
    }

    public async Task<GitHubIssue> CreateIssueAsync(string repositoryFullName, string title, string body, IReadOnlyList<string> labels, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/issues",
            JsonContent.Create(new Api.CreateIssueRequest(title, body, labels)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubIssue>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>
    /// Finds the most recent issue with <paramref name="label"/> whose body contains <paramref name="marker"/> among the
    /// issues created or updated since <paramref name="since"/>, or <c>null</c>. Uses the issues list (immediately
    /// consistent, unlike search); the time filter keeps it short however many issues the label has.
    /// </summary>
    public async Task<GitHubIssue?> FindIssueByMarkerAsync(string repositoryFullName, string label, string marker, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        string? url = $"repos/{repositoryFullName}/issues?labels={Uri.EscapeDataString(label)}&state=all&sort=created&direction=desc&per_page={PageSize}"
            + $"&since={Uri.EscapeDataString(since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}";
        while (url is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var issues = await response.Content.ReadFromJsonAsync<List<GitHubIssue>>(cancellationToken) ?? [];
            if (issues.FirstOrDefault(i => i.Body?.Contains(marker, StringComparison.Ordinal) == true) is { } found)
            {
                return found;
            }
            url = GetNextLink(response);
        }

        return null;
    }

    public async Task<GitHubIssue> GetIssueAsync(string repositoryFullName, int issueNumber, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}/issues/{issueNumber}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubIssue>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>Makes an issue a sub-issue of <paramref name="parentIssueNumber"/>.</summary>
    /// <param name="subIssueId">GitHub's id of the child issue (not its number).</param>
    public async Task AddSubIssueAsync(string repositoryFullName, int parentIssueNumber, long subIssueId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/issues/{parentIssueNumber}/sub_issues",
            JsonContent.Create(new Api.AddSubIssueRequest(subIssueId)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Assigns the Copilot cloud agent to an issue, optionally with a custom agent and model.
    /// GitHub silently ignores assignees it can't assign, so check <see cref="IsAssignedToCopilot"/> on the result.
    /// </summary>
    public async Task<GitHubIssue> AssignCopilotAsync(string repositoryFullName, int issueNumber, CopilotAssignment assignment, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/issues/{issueNumber}/assignees",
            JsonContent.Create(new Api.AssignRequest([CopilotLogin], assignment)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubIssue>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    public const string CopilotLogin = "copilot-swe-agent[bot]";

    public static bool IsAssignedToCopilot(GitHubIssue issue)
    {
        return issue.Assignees?.Any(a => a.Login.Contains("copilot", StringComparison.OrdinalIgnoreCase)) == true;
    }

    public async Task<CopilotCodeReviewSettings> GetCopilotCodeReviewSettingsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, CopilotCodeReviewSettingsUrl(repositoryFullName), null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CopilotCodeReviewSettings>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>Changes only the settings set in <paramref name="changes"/> and returns all settings as they are now.</summary>
    public async Task<CopilotCodeReviewSettings> UpdateCopilotCodeReviewSettingsAsync(string repositoryFullName, CopilotCodeReviewSettingsUpdate changes, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Patch, CopilotCodeReviewSettingsUrl(repositoryFullName), JsonContent.Create(changes), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CopilotCodeReviewSettings>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    private static string CopilotCodeReviewSettingsUrl(string repositoryFullName)
    {
        return $"repos/{repositoryFullName}/copilot/code-review/settings";
    }

    /// <summary>The pull requests (open and closed) updated at or after <paramref name="since"/>, most recently updated first.</summary>
    public async Task<IReadOnlyList<GitHubPullRequest>> GetPullRequestsUpdatedSinceAsync(string repositoryFullName, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var pullRequests = new List<GitHubPullRequest>();
        string? url = $"repos/{repositoryFullName}/pulls?state=all&sort=updated&direction=desc&per_page={PageSize}";
        while (url is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<List<GitHubPullRequest>>(cancellationToken) ?? [];
            pullRequests.AddRange(page.Where(p => p.UpdatedAt >= since));
            // Sorted by update time, so the first older one means every next page is older too.
            url = page.Any(p => p.UpdatedAt < since) ? null : GetNextLink(response);
        }
        return pullRequests;
    }

    private const string LinkedPullRequestsQuery = """
        query($owner: String!, $name: String!, $number: Int!) {
          repository(owner: $owner, name: $name) {
            issue(number: $number) {
              closedByPullRequestsReferences(first: 10, includeClosedPrs: true) {
                nodes {
                  number url title state isDraft headRefName updatedAt reviewDecision
                  latestReviews(first: 20) { nodes { state } }
                  commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
                }
              }
            }
          }
        }
        """;

    /// <summary>
    /// The pull requests (open, merged and closed) that close the issue when merged, e.g. because their body says
    /// <c>Fixes #n</c> like Copilot's do. One GraphQL request.
    /// </summary>
    public async Task<IReadOnlyList<GitHubLinkedPullRequest>> GetLinkedPullRequestsAsync(string repositoryFullName, int issueNumber, CancellationToken cancellationToken = default)
    {
        var (owner, name) = SplitFullName(repositoryFullName);
        using var response = await SendAsync(HttpMethod.Post, "graphql",
            JsonContent.Create(new Api.GraphQlRequest(LinkedPullRequestsQuery, new { owner, name, number = issueNumber })), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<Api.GraphQlResponse<Api.LinkedPullRequestsData>>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");

        // GraphQL answers 200 with errors instead of an error status.
        var issue = result.Data?.Repository?.Issue;
        if (issue is null || result.Errors is { Count: > 0 })
        {
            var error = result.Errors?.FirstOrDefault();
            var status = error?.Type switch
            {
                "NOT_FOUND" => HttpStatusCode.NotFound,
                "FORBIDDEN" => HttpStatusCode.Forbidden,
                "RATE_LIMITED" => HttpStatusCode.TooManyRequests,
                _ => HttpStatusCode.BadGateway
            };
            throw new GitHubApiException(status, $"GitHub couldn't list the pull requests of issue #{issueNumber}: {error?.Message ?? "no data returned"}");
        }

        return (issue.PullRequests?.Nodes ?? []).OfType<Api.LinkedPullRequestNode>().Select(pr => new GitHubLinkedPullRequest(
            pr.Number, pr.Url, pr.Title, pr.State, pr.IsDraft, pr.HeadRefName, pr.UpdatedAt, pr.ReviewDecision,
            (pr.LatestReviews?.Nodes ?? []).Select(r => r?.State).OfType<string>().ToList(),
            pr.Commits?.Nodes?.LastOrDefault()?.Commit?.StatusCheckRollup?.State)).ToList();
    }

    /// <summary>
    /// Copilot's latest agent session on <paramref name="branch"/>: the newest Actions run that GitHub started for the
    /// Copilot cloud agent (a <c>dynamic</c> run) rather than for a workflow file of the repository.
    /// </summary>
    /// <returns>
    /// No run when there is none or the token may not read Actions; the latter only hides the run, nothing else, and is
    /// told by <see cref="CopilotRunLookup.CanReadActions"/>.
    /// </returns>
    public async Task<CopilotRunLookup> GetLatestCopilotRunAsync(string repositoryFullName, string branch, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"repos/{repositoryFullName}/actions/runs?branch={Uri.EscapeDataString(branch)}&per_page=30", null, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden && !await IsRateLimitedAsync(response, cancellationToken))
        {
            return new(null, CanReadActions: false);
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var runs = (await response.Content.ReadFromJsonAsync<Api.WorkflowRunsResponse>(cancellationToken))?.WorkflowRuns ?? [];
        return new(runs.Where(IsCopilotRun).MaxBy(r => r.CreatedAt), CanReadActions: true);
    }

    /// <summary>The open pull requests of a repository, most recently updated first.</summary>
    public async Task<IReadOnlyList<GitHubPullRequest>> GetOpenPullRequestsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return await GetAllPagesAsync<GitHubPullRequest>($"repos/{repositoryFullName}/pulls?state=open&sort=updated&direction=desc&per_page={PageSize}", cancellationToken);
    }

    /// <summary>
    /// Copilot's agent sessions that are running or waiting to run in the repository: its in-progress and queued
    /// Actions runs (one request each).
    /// </summary>
    /// <returns><c>null</c> when the token may not read Actions.</returns>
    public async Task<IReadOnlyList<GitHubWorkflowRun>?> GetActiveCopilotRunsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        var runs = new List<GitHubWorkflowRun>();
        foreach (var status in new[] { "in_progress", "queued" })
        {
            using var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}/actions/runs?status={status}&per_page={PageSize}", null, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden && !await IsRateLimitedAsync(response, cancellationToken))
            {
                return null;
            }
            await EnsureSuccessAsync(response, cancellationToken);
            runs.AddRange((await response.Content.ReadFromJsonAsync<Api.WorkflowRunsResponse>(cancellationToken))?.WorkflowRuns ?? []);
        }
        // A run can move from queued to in progress between the two requests.
        return runs.Where(IsCopilotRun).DistinctBy(r => r.Id).ToList();
    }

    public static bool IsCopilotRun(GitHubWorkflowRun run)
    {
        return string.Equals(run.Event, "dynamic", StringComparison.OrdinalIgnoreCase)
            && (run.Path?.Contains("copilot", StringComparison.OrdinalIgnoreCase) == true || run.Name?.Contains("copilot", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static async Task<bool> IsRateLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0")
        {
            return true;
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Owner, string Name) SplitFullName(string repositoryFullName)
    {
        var parts = repositoryFullName.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"'{repositoryFullName}' is not an owner/name repository name.", nameof(repositoryFullName));
        }
        return (parts[0], parts[1]);
    }

    public async Task<GitHubPullRequestDetails> GetPullRequestAsync(string repositoryFullName, int number, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}/pulls/{number}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubPullRequestDetails>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>GitHub lists at most this many files of a pull request.</summary>
    public const int MaxPullRequestFiles = 3000;

    /// <summary>The files of a pull request's diff with their patches, in GitHub's order (at most <see cref="MaxPullRequestFiles"/>).</summary>
    public async Task<IReadOnlyList<GitHubPullRequestFile>> GetPullRequestFilesAsync(string repositoryFullName, int number, CancellationToken cancellationToken = default)
    {
        return await GetAllPagesAsync<GitHubPullRequestFile>($"repos/{repositoryFullName}/pulls/{number}/files?per_page={PageSize}", cancellationToken);
    }

    /// <summary>The comments of the pull request's (or issue's) conversation, oldest first.</summary>
    public async Task<IReadOnlyList<GitHubComment>> GetIssueCommentsAsync(string repositoryFullName, int number, CancellationToken cancellationToken = default)
    {
        return await GetAllPagesAsync<GitHubComment>($"repos/{repositoryFullName}/issues/{number}/comments?per_page={PageSize}", cancellationToken);
    }

    /// <summary>The comments on lines of the pull request's diff, oldest first.</summary>
    public async Task<IReadOnlyList<GitHubComment>> GetPullRequestReviewCommentsAsync(string repositoryFullName, int number, CancellationToken cancellationToken = default)
    {
        return await GetAllPagesAsync<GitHubComment>($"repos/{repositoryFullName}/pulls/{number}/comments?per_page={PageSize}", cancellationToken);
    }

    /// <summary>Adds a comment to the conversation of a pull request (or issue).</summary>
    /// <returns>The comment's URL.</returns>
    public async Task<string> CreateIssueCommentAsync(string repositoryFullName, int number, string body, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/issues/{number}/comments",
            JsonContent.Create(new Api.CreateCommentRequest(body)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<Api.HtmlUrlResponse>(cancellationToken))?.HtmlUrl
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>Submits a review with comments on lines of the diff.</summary>
    /// <param name="requestChanges">Request changes instead of only commenting.</param>
    /// <returns>The review's URL.</returns>
    public async Task<string> CreatePullRequestReviewAsync(string repositoryFullName, int number, string body, IReadOnlyList<GitHubReviewLineComment> comments,
        bool requestChanges, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/pulls/{number}/reviews",
            JsonContent.Create(new Api.CreateReviewRequest(body, requestChanges ? "REQUEST_CHANGES" : "COMMENT", comments)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<Api.HtmlUrlResponse>(cancellationToken))?.HtmlUrl
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>Submits a review that approves the pull request, optionally with a comment.</summary>
    /// <returns>The review's URL.</returns>
    public async Task<string> ApprovePullRequestAsync(string repositoryFullName, int number, string body, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/pulls/{number}/reviews",
            JsonContent.Create(new Api.CreateReviewRequest(body, "APPROVE", [])), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<Api.HtmlUrlResponse>(cancellationToken))?.HtmlUrl
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    private async Task<List<T>> GetAllPagesAsync<T>(string firstUrl, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        string? url = firstUrl;
        while (url is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            items.AddRange(await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken) ?? []);
            url = GetNextLink(response);
        }
        return items;
    }

    public async Task<IReadOnlyList<GitHubPullRequestReview>> GetPullRequestReviewsAsync(string repositoryFullName, int number, CancellationToken cancellationToken = default)
    {
        var reviews = new List<GitHubPullRequestReview>();
        string? url = $"repos/{repositoryFullName}/pulls/{number}/reviews?per_page={PageSize}";
        while (url is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            reviews.AddRange(await response.Content.ReadFromJsonAsync<List<GitHubPullRequestReview>>(cancellationToken) ?? []);
            url = GetNextLink(response);
        }
        return reviews;
    }

    /// <summary>The REST API version the Copilot usage metrics and billing usage endpoints are documented for.</summary>
    public const string UsageApiVersion = "2026-03-10";

    /// <summary>
    /// Gets the organization's <c>repos-1-day</c> Copilot usage metrics report: one row per repository with pull request
    /// activity that day. <c>null</c> when GitHub has no report for the day (204).
    /// </summary>
    public async Task<IReadOnlyList<Api.RepositoryMetricsRow>?> GetCopilotRepositoryMetricsReportAsync(string organization, DateOnly day, CancellationToken cancellationToken = default)
    {
        var url = $"orgs/{Uri.EscapeDataString(organization)}/copilot/metrics/reports/repos-1-day?day={day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken, apiVersion: UsageApiVersion);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var report = await response.Content.ReadFromJsonAsync<Api.MetricsReportResponse>(cancellationToken);
        var rows = new List<Api.RepositoryMetricsRow>();
        foreach (var link in report?.DownloadLinks ?? [])
        {
            rows.AddRange(await DownloadNdjsonAsync<Api.RepositoryMetricsRow>(link, cancellationToken));
        }
        return rows;
    }

    /// <summary>Downloads a report file from a signed link, without the GitHub token: the link points outside GitHub's API.</summary>
    private async Task<List<T>> DownloadNdjsonAsync<T>(string link, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new GitHubApiException(HttpStatusCode.BadGateway, "GitHub returned an invalid report download link.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubApiException(response.StatusCode, $"Downloading the Copilot metrics report failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var rows = new List<T>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line) && JsonSerializer.Deserialize<T>(line) is { } row)
            {
                rows.Add(row);
            }
        }
        return rows;
    }

    /// <summary>
    /// Gets the premium request or AI credit usage of an organization or user for one month. For a user, it only covers the
    /// Copilot plans the user bought personally.
    /// </summary>
    public async Task<IReadOnlyList<CopilotBillingUsageItem>> GetCopilotBillingUsageAsync(string account, bool organization, CopilotBillingReport report, int year, int month, CancellationToken cancellationToken = default)
    {
        var owner = organization ? $"organizations/{Uri.EscapeDataString(account)}" : $"users/{Uri.EscapeDataString(account)}";
        var path = report switch
        {
            CopilotBillingReport.PremiumRequests => "premium_request",
            CopilotBillingReport.AiCredits => "ai_credit",
            _ => throw new ArgumentOutOfRangeException(nameof(report), report, null)
        };
        using var response = await SendAsync(HttpMethod.Get, $"{owner}/settings/billing/{path}/usage?year={year}&month={month}", null, cancellationToken, apiVersion: UsageApiVersion);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<Api.BillingUsageResponse>(cancellationToken))?.UsageItems ?? [];
    }

    /// <param name="accept">A media type instead of the client's default <c>application/vnd.github+json</c>.</param>
    /// <param name="apiVersion">A REST API version instead of the client's default one.</param>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken, string? accept = null, string? apiVersion = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetAccessTokenAsync());
        // A header on the request replaces the client's default header of the same name.
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }
        if (apiVersion is not null)
        {
            request.Headers.Add(ApiVersionHeader, apiVersion);
        }
        return await httpClient.SendAsync(request, cancellationToken);
    }

    public const string ApiVersionHeader = "X-GitHub-Api-Version";

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        try
        {
            detail = (await response.Content.ReadFromJsonAsync<Api.ErrorResponse>(cancellationToken))?.Message;
        }
        catch
        {
            // Not a JSON error body; fall back to the status code.
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your GitHub session has expired. Please sign out and sign in again.",
            HttpStatusCode.Conflict => "The file was changed on GitHub in the meantime. Reload and try again.",
            _ => $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}{(detail is null ? string.Empty : $": {detail}")}"
        };

        throw new GitHubApiException(response.StatusCode, message);
    }

    private static string ContentsUrl(string repositoryFullName, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Uri collapses dot segments even when escaped, so '..' would leave the contents API (e.g. 'contents/../../../user').
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException($"The path '{path}' can't contain '.' or '..' segments.", nameof(path));
        }

        return $"repos/{repositoryFullName}/contents/{string.Join('/', segments.Select(Uri.EscapeDataString))}";
    }

    private static string? GetNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        // Format: <https://api.github.com/...&page=2>; rel="next", <...>; rel="last". The URLs themselves can contain
        // commas (e.g. affiliation=owner,collaborator), so match whole <url>; rel="..." entries instead of splitting on ','.
        foreach (Match link in LinkEntry().Matches(string.Join(", ", values)))
        {
            if (link.Groups["rel"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("next"))
            {
                return link.Groups["url"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex("""<(?<url>[^>]*)>\s*;\s*rel="(?<rel>[^"]*)"\s*""")]
    private static partial Regex LinkEntry();

}

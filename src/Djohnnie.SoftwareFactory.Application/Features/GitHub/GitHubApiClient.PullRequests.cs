using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api = Djohnnie.SoftwareFactory.Contracts.GitHub.Api;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>Pull requests: listing, opening, files, comments, reviews, marking ready for review, Copilot code review requests and merging.</summary>
public sealed partial class GitHubApiClient
{
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
                  number url title state isDraft headRefName updatedAt reviewDecision mergeable
                  latestReviews(first: 20) { nodes { state } }
                  reviewRequests(first: 20) { nodes { requestedReviewer { __typename ... on Bot { login } ... on User { login } } } }
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
            JsonContent.Create(new Api.GraphQlRequest(LinkedPullRequestsQuery, new { owner, name, number = issueNumber })), cancellationToken, readOnly: true);
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
            pr.Commits?.Nodes?.LastOrDefault()?.Commit?.StatusCheckRollup?.State,
            (pr.ReviewRequests?.Nodes ?? []).Any(r => r?.RequestedReviewer is { } reviewer
                && new GitHubOwner(reviewer.Login ?? string.Empty, null, reviewer.TypeName).IsCopilot),
            pr.Mergeable)).ToList();
    }

    /// <summary>The open pull requests of a repository, most recently updated first.</summary>
    public async Task<IReadOnlyList<GitHubPullRequest>> GetOpenPullRequestsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return await GetAllPagesAsync<GitHubPullRequest>($"repos/{repositoryFullName}/pulls?state=open&sort=updated&direction=desc&per_page={PageSize}", cancellationToken);
    }

    /// <summary>Opens a pull request from <paramref name="head"/> (a branch of the same repository) into <paramref name="baseBranch"/>.</summary>
    public async Task<GitHubPullRequestDetails> CreatePullRequestAsync(string repositoryFullName, string title, string head, string baseBranch, string body,
        bool draft, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/pulls",
            JsonContent.Create(new Api.CreatePullRequestRequest(title, head, baseBranch, body, draft)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubPullRequestDetails>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
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

    /// <summary>Replies in the thread of a comment on a line of the pull request's diff.</summary>
    /// <param name="commentId">The thread's first comment: GitHub doesn't accept replies to replies.</param>
    /// <returns>The reply's URL.</returns>
    public async Task<string> ReplyToReviewCommentAsync(string repositoryFullName, int number, long commentId, string body, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/pulls/{number}/comments/{commentId}/replies",
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

    /// <summary>
    /// Asks Copilot code review to review the pull request (again). The token's account needs Copilot code review and write
    /// access to pull requests; GitHub answers 422 otherwise.
    /// </summary>
    public async Task RequestCopilotReviewAsync(string repositoryFullName, int number, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/pulls/{number}/requested_reviewers",
            JsonContent.Create(new Api.RequestReviewersRequest([CopilotReviewerLogin])), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private const string MarkReadyForReviewMutation = """
        mutation($id: ID!) {
          markPullRequestReadyForReview(input: { pullRequestId: $id }) {
            pullRequest { isDraft }
          }
        }
        """;

    /// <summary>
    /// Marks a draft pull request ready for review, so it can be merged; only GraphQL can. The token needs write access to
    /// pull requests.
    /// </summary>
    /// <param name="nodeId">The pull request's GraphQL id (<see cref="GitHubPullRequestDetails.NodeId"/>), not its number.</param>
    /// <exception cref="GitHubApiException">GitHub refused it, e.g. 403 without write access or 404 for an unknown pull request.</exception>
    public async Task MarkPullRequestReadyForReviewAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "graphql",
            JsonContent.Create(new Api.GraphQlRequest(MarkReadyForReviewMutation, new { id = nodeId })), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<Api.GraphQlResponse<JsonElement>>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");

        // GraphQL answers 200 with errors instead of an error status.
        if (result.Errors is { Count: > 0 } errors)
        {
            var error = errors[0];
            var status = error.Type switch
            {
                "NOT_FOUND" => HttpStatusCode.NotFound,
                "FORBIDDEN" => HttpStatusCode.Forbidden,
                "RATE_LIMITED" => HttpStatusCode.TooManyRequests,
                _ => HttpStatusCode.UnprocessableEntity
            };
            throw new GitHubApiException(status, $"GitHub couldn't mark the pull request ready for review: {error.Message ?? "no reason given"}");
        }
    }

    /// <summary>The reviewer to request for a Copilot code review.</summary>
    public const string CopilotReviewerLogin = "copilot-pull-request-reviewer[bot]";

    /// <summary>Merges the pull request, which closes it.</summary>
    /// <param name="mergeMethod"><c>merge</c>, <c>squash</c> or <c>rebase</c>.</param>
    /// <returns>The commit the merge created on the base branch.</returns>
    /// <exception cref="GitHubApiException">
    /// 405 when GitHub can't merge it (e.g. a draft, conflicts, a branch rule, or a merge method the repository doesn't allow),
    /// 409 when its branch moved on in the meantime.
    /// </exception>
    public async Task<string?> MergePullRequestAsync(string repositoryFullName, int number, string mergeMethod, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Put, $"repos/{repositoryFullName}/pulls/{number}/merge",
            JsonContent.Create(new Api.MergePullRequestRequest(mergeMethod)), cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // EnsureSuccessAsync would call this a file change.
            throw new GitHubApiException(response.StatusCode, "The pull request's branch changed in the meantime. Reload and review the latest changes, then try again.");
        }
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<Api.MergePullRequestResponse>(cancellationToken);
        if (result is not { Merged: true })
        {
            throw new GitHubApiException(response.StatusCode, result?.Message ?? "GitHub didn't merge the pull request.");
        }
        return result.Sha;
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
}

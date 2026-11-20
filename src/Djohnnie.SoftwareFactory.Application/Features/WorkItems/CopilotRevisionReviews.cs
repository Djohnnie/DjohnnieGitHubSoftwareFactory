using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Asks Copilot code review to review a pull request once the Copilot cloud agent has finished the revision that feedback
/// sent from DGHSF asked for (<see cref="WorkItem.CopilotRevisionRequestedAt"/>). Copilot's agent session for the feedback is
/// its first Actions run on the pull request's branch that was created after the feedback was sent; the DGHSF agent worker
/// requests its reviews itself.
/// </summary>
public sealed class CopilotRevisionReviews(IWorkItemStore store, GitHubApiClient gitHub, TimeProvider time)
{
    /// <summary>How long Copilot may take to start working on the feedback before DGHSF stops waiting for it.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromHours(6);

    /// <summary>
    /// Requests the review when Copilot's agent session for the feedback has succeeded, or gives up on it when it can't
    /// come (the pull request is no longer open, the session failed or never started, GitHub refuses). Either way the item
    /// no longer waits for it; while Copilot is still busy nothing changes.
    /// </summary>
    /// <exception cref="GitHubApiException">GitHub refused the token, the rate limit was reached or GitHub had an error.</exception>
    public async Task<CopilotRevisionReviewResult> RequestWhenFinishedAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
        if (item.CopilotRevisionRequestedAt is not { } requestedAt)
        {
            return CopilotRevisionReviewResult.NotDue;
        }

        var number = item.PullRequestNumber;
        if (item.Progress is CopilotProgress.Merged or CopilotProgress.Closed || number is null || item.PullRequestBranch is not { } branch)
        {
            return await AbandonAsync(item, number is null
                ? "the item no longer has a pull request."
                : $"pull request #{number} is {(item.Progress == CopilotProgress.Merged ? "merged" : "closed")}.", cancellationToken);
        }

        var lookup = await gitHub.GetLatestCopilotRunAsync(repositoryFullName, branch, cancellationToken);
        if (!lookup.CanReadActions)
        {
            return await AbandonAsync(item, "GitHub refused to list the Actions runs, so it is unknown when Copilot finished the revision.", cancellationToken);
        }
        if (lookup.Run is not { } run || run.CreatedAt < requestedAt)
        {
            return time.GetUtcNow() - requestedAt > StartTimeout
                ? await AbandonAsync(item, $"Copilot didn't start working on the feedback within {StartTimeout.TotalHours:N0} hours.", cancellationToken)
                : new(CopilotRevisionReviewOutcome.Waiting, number);
        }
        if (!string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return new(CopilotRevisionReviewOutcome.Waiting, number);
        }
        if (!string.Equals(run.Conclusion, "success", StringComparison.OrdinalIgnoreCase))
        {
            return await AbandonAsync(item, $"Copilot's agent session ended with '{run.Conclusion ?? "no conclusion"}' ({run.HtmlUrl}).", cancellationToken);
        }

        try
        {
            await gitHub.RequestCopilotReviewAsync(repositoryFullName, number.Value, cancellationToken);
        }
        catch (GitHubApiException ex) when (IsRefusal(ex))
        {
            return await AbandonAsync(item, $"GitHub refused to request the review: {ex.Message}", cancellationToken);
        }
        await store.SetCopilotRevisionRequestedAsync(repositoryFullName, id, null, cancellationToken);
        return new(CopilotRevisionReviewOutcome.Requested, number);
    }

    private async Task<CopilotRevisionReviewResult> AbandonAsync(WorkItem item, string reason, CancellationToken cancellationToken)
    {
        await store.SetCopilotRevisionRequestedAsync(item.RepositoryFullName, item.Id, null, cancellationToken);
        return new(CopilotRevisionReviewOutcome.Abandoned, item.PullRequestNumber, reason);
    }

    /// <summary>
    /// GitHub won't request this review, e.g. a token without write access or Copilot code review (403, 404, 422); trying
    /// again at the next sync wouldn't help. A rate limit or an expired token is left to the caller.
    /// </summary>
    private static bool IsRefusal(GitHubApiException ex)
    {
        return ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity
            || (ex.StatusCode == HttpStatusCode.Forbidden && !ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
    }
}

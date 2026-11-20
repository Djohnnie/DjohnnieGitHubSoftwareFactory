using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Hands a pull request of the Copilot cloud agent over for review once one of Copilot's agent sessions on it has finished:
/// it marks the pull request ready for review when it is a draft (Copilot opens them as drafts, which can't be merged), and
/// asks Copilot code review to review it. Copilot's agent sessions are its Actions runs on the pull request's branch; every
/// finished one is handled once (<see cref="WorkItem.CopilotSessionHandledAt"/>), both its first session on the issue and its
/// revisions. After feedback sent from DGHSF (<see cref="WorkItem.CopilotRevisionRequestedAt"/>) the session for that feedback is
/// awaited: its first run created after the feedback was sent. The DGHSF agent worker requests its reviews itself.
/// </summary>
public sealed class CopilotRevisionReviews(IWorkItemStore store, GitHubApiClient gitHub, TimeProvider time)
{
    /// <summary>How long Copilot may take to start working on the feedback before DGHSF stops waiting for it.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromHours(6);

    /// <summary>
    /// After feedback, see <see cref="AfterFeedbackAsync"/>. Otherwise, for an item of the Copilot cloud agent with an open
    /// pull request, once Copilot's latest agent session has succeeded and wasn't handled yet: marks the pull request ready for
    /// review, and asks Copilot code review to review it unless it was asked already or reviewed it after the session. While
    /// Copilot is busy, or when its session failed, nothing changes.
    /// </summary>
    /// <exception cref="GitHubApiException">GitHub refused the token, the rate limit was reached or GitHub had an error.</exception>
    public async Task<CopilotRevisionReviewResult> RequestWhenFinishedAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
        if (item.CopilotRevisionRequestedAt is { } requestedAt)
        {
            return await AfterFeedbackAsync(item, requestedAt, cancellationToken);
        }

        if (item is not { Executor: AgentExecutor.CopilotCloudAgent, Status: WorkItemStatus.InProgress, PullRequestNumber: { } number, PullRequestBranch: { } branch }
            || item.Progress is not (CopilotProgress.ReadyForReview or CopilotProgress.ChangesRequested or CopilotProgress.Approved or CopilotProgress.CopilotReviewing))
        {
            return CopilotRevisionReviewResult.NotDue;
        }

        // Without access to Actions it is unknown when Copilot finished; the sync already warns about the missing progress.
        var lookup = await gitHub.GetLatestCopilotRunAsync(item.RepositoryFullName, branch, cancellationToken);
        if (lookup.Run is not { IsCompleted: true } run
            || !string.Equals(run.Conclusion, "success", StringComparison.OrdinalIgnoreCase)
            || run.CreatedAt <= (item.CopilotSessionHandledAt ?? DateTimeOffset.MinValue))
        {
            return CopilotRevisionReviewResult.NotDue;
        }

        var (markedReady, problem) = await MarkReadyForReviewAsync(item.RepositoryFullName, number, cancellationToken);
        if (await IsReviewedAfterAsync(item, number, run, cancellationToken))
        {
            await store.SetCopilotSessionHandledAsync(item.RepositoryFullName, item.Id, run.CreatedAt, cancellationToken);
            return new(CopilotRevisionReviewOutcome.AlreadyReviewed, number, problem, markedReady);
        }

        try
        {
            await gitHub.RequestCopilotReviewAsync(item.RepositoryFullName, number, cancellationToken);
        }
        catch (GitHubApiException ex) when (IsRefusal(ex))
        {
            await store.SetCopilotSessionHandledAsync(item.RepositoryFullName, item.Id, run.CreatedAt, cancellationToken);
            return new(CopilotRevisionReviewOutcome.Abandoned, number, $"GitHub refused to request the review: {ex.Message}", markedReady);
        }
        await store.SetCopilotSessionHandledAsync(item.RepositoryFullName, item.Id, run.CreatedAt, cancellationToken);
        return new(CopilotRevisionReviewOutcome.Requested, number, problem, markedReady);
    }

    /// <summary>
    /// Requests the review when Copilot's agent session for the feedback has succeeded (marking a draft ready for review first),
    /// or gives up on it when it can't come (the pull request is no longer open, the session failed or never started, GitHub
    /// refuses). Either way the item no longer waits for it; while Copilot is still busy nothing changes.
    /// </summary>
    private async Task<CopilotRevisionReviewResult> AfterFeedbackAsync(WorkItem item, DateTimeOffset requestedAt, CancellationToken cancellationToken)
    {
        var number = item.PullRequestNumber;
        if (item.Progress is CopilotProgress.Merged or CopilotProgress.Closed || number is null || item.PullRequestBranch is not { } branch)
        {
            return await AbandonAsync(item, null, number is null
                ? "the item no longer has a pull request."
                : $"pull request #{number} is {(item.Progress == CopilotProgress.Merged ? "merged" : "closed")}.", cancellationToken);
        }

        var lookup = await gitHub.GetLatestCopilotRunAsync(item.RepositoryFullName, branch, cancellationToken);
        if (!lookup.CanReadActions)
        {
            return await AbandonAsync(item, null, "GitHub refused to list the Actions runs, so it is unknown when Copilot finished the revision.", cancellationToken);
        }
        if (lookup.Run is not { } run || run.CreatedAt < requestedAt)
        {
            return time.GetUtcNow() - requestedAt > StartTimeout
                ? await AbandonAsync(item, null, $"Copilot didn't start working on the feedback within {StartTimeout.TotalHours:N0} hours.", cancellationToken)
                : new(CopilotRevisionReviewOutcome.Waiting, number, AfterFeedback: true);
        }
        if (!run.IsCompleted)
        {
            return new(CopilotRevisionReviewOutcome.Waiting, number, AfterFeedback: true);
        }
        if (!string.Equals(run.Conclusion, "success", StringComparison.OrdinalIgnoreCase))
        {
            return await AbandonAsync(item, run, $"Copilot's agent session ended with '{run.Conclusion ?? "no conclusion"}' ({run.HtmlUrl}).", cancellationToken);
        }

        var (markedReady, problem) = await MarkReadyForReviewAsync(item.RepositoryFullName, number.Value, cancellationToken);
        try
        {
            await gitHub.RequestCopilotReviewAsync(item.RepositoryFullName, number.Value, cancellationToken);
        }
        catch (GitHubApiException ex) when (IsRefusal(ex))
        {
            return await AbandonAsync(item, run, $"GitHub refused to request the review: {ex.Message}", cancellationToken, markedReady);
        }
        await store.SetCopilotSessionHandledAsync(item.RepositoryFullName, item.Id, run.CreatedAt, cancellationToken);
        await store.SetCopilotRevisionRequestedAsync(item.RepositoryFullName, item.Id, null, cancellationToken);
        return new(CopilotRevisionReviewOutcome.Requested, number, problem, markedReady, AfterFeedback: true);
    }

    /// <param name="run">The session for the feedback, which then counts as handled; <c>null</c> when there is none.</param>
    private async Task<CopilotRevisionReviewResult> AbandonAsync(WorkItem item, GitHubWorkflowRun? run, string reason, CancellationToken cancellationToken,
        bool markedReady = false)
    {
        if (run is not null)
        {
            await store.SetCopilotSessionHandledAsync(item.RepositoryFullName, item.Id, run.CreatedAt, cancellationToken);
        }
        await store.SetCopilotRevisionRequestedAsync(item.RepositoryFullName, item.Id, null, cancellationToken);
        return new(CopilotRevisionReviewOutcome.Abandoned, item.PullRequestNumber, reason, markedReady, AfterFeedback: true);
    }

    /// <summary>
    /// Marks the pull request ready for review when it is an open draft, first, so Copilot code review reviews the pull
    /// request as it will be merged. GitHub refusing it doesn't stop the review from being requested.
    /// </summary>
    /// <returns>Whether it was marked ready, and why it couldn't be when GitHub refused.</returns>
    private async Task<(bool MarkedReady, string? Problem)> MarkReadyForReviewAsync(string repositoryFullName, int number, CancellationToken cancellationToken)
    {
        var pullRequest = await gitHub.GetPullRequestAsync(repositoryFullName, number, cancellationToken);
        if (!pullRequest.IsOpen || !pullRequest.Draft)
        {
            return (false, null);
        }
        if (pullRequest.NodeId is not { Length: > 0 } nodeId)
        {
            return (false, $"GitHub didn't return the id of pull request #{number}, so it couldn't be marked ready for review.");
        }

        try
        {
            await gitHub.MarkPullRequestReadyForReviewAsync(nodeId, cancellationToken);
            return (true, null);
        }
        catch (GitHubApiException ex) when (IsRefusal(ex))
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Copilot code review was asked to review the pull request and hasn't yet (as of this sync's refresh), or submitted a
    /// review once the session had finished, e.g. because a ruleset asks for one on every push.
    /// </summary>
    private async Task<bool> IsReviewedAfterAsync(WorkItem item, int number, GitHubWorkflowRun run, CancellationToken cancellationToken)
    {
        if (item.Progress == CopilotProgress.CopilotReviewing)
        {
            return true;
        }
        var finishedAt = run.UpdatedAt ?? run.CreatedAt;
        var reviews = await gitHub.GetPullRequestReviewsAsync(item.RepositoryFullName, number, cancellationToken);
        return reviews.Any(r => r.User?.IsCopilot == true && r.State != "PENDING" && r.SubmittedAt >= finishedAt);
    }

    /// <summary>
    /// GitHub won't do it, e.g. a token without write access or Copilot code review (403, 404, 422); trying again at the next
    /// sync wouldn't help. A rate limit or an expired token is left to the caller.
    /// </summary>
    private static bool IsRefusal(GitHubApiException ex)
    {
        return ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity
            || (ex.StatusCode == HttpStatusCode.Forbidden && !ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
    }
}

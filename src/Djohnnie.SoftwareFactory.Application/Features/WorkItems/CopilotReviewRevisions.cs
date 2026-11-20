using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Starts a revision run of the DGHSF agent worker by itself once Copilot code review has reviewed the worker's pull request
/// with remarks, for items with <see cref="WorkItem.ReviseOnCopilotReview"/>. The worker asks Copilot code review to review
/// every revision it pushed, so the review and revision rounds would go on as long as Copilot finds something: only
/// <see cref="MaxRounds"/> rounds of remarks are revised automatically, after that the user starts the next revision.
/// </summary>
public sealed class CopilotReviewRevisions(IWorkItemStore store, IAgentRunStore agentRuns, GitHubApiClient gitHub, WorkItemWorkflow workflow)
{
    /// <summary>How many reviews with remarks of Copilot code review on one pull request are revised automatically.</summary>
    public const int MaxRounds = 3;

    /// <summary>
    /// Starts the revision when Copilot code review submitted a review with remarks (line comments, or requested changes)
    /// after the worker's last run on the pull request ended and after the review the sync acted on last. Every review is
    /// acted on once (<see cref="WorkItem.CopilotReviewHandledAt"/>), also when no revision could be started.
    /// </summary>
    /// <exception cref="GitHubApiException">GitHub refused the token, the rate limit was reached or GitHub had an error.</exception>
    public async Task<CopilotReviewRevisionResult> StartWhenReviewedAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
        if (item is not { ReviseOnCopilotReview: true, Executor: AgentExecutor.DghsfWorker, Status: WorkItemStatus.InProgress }
            || item.Progress is CopilotProgress.Merged or CopilotProgress.Closed)
        {
            return CopilotReviewRevisionResult.NotDue;
        }

        // While the worker runs, it isn't started again; a review that comes in meanwhile is part of the feedback of the next one.
        var runs = await agentRuns.GetForWorkItemAsync(repositoryFullName, id, cancellationToken);
        if (runs.Any(r => r.IsActive) || runs.FirstOrDefault(r => r.PullRequestNumber is not null)?.PullRequestNumber is not { } number)
        {
            return CopilotReviewRevisionResult.NotDue;
        }

        var since = new[] { PullRequestDiscussion.HandledUntil(runs, number), item.CopilotReviewHandledAt ?? DateTimeOffset.MinValue }
            .Concat(runs.Select(r => r.CompletedAt ?? DateTimeOffset.MinValue))
            .Max();
        var reviews = (await gitHub.GetPullRequestReviewsAsync(repositoryFullName, number, cancellationToken))
            .Where(r => r.User?.IsCopilot == true && r.SubmittedAt is not null && r.State != "PENDING")
            .OrderBy(r => r.SubmittedAt)
            .ToList();
        var fresh = reviews.Where(r => r.SubmittedAt > since).ToList();
        if (fresh.Count == 0)
        {
            return CopilotReviewRevisionResult.NotDue;
        }

        var lineComments = await gitHub.GetPullRequestReviewCommentsAsync(repositoryFullName, number, cancellationToken);
        var handledAt = fresh[^1].SubmittedAt!.Value;
        if (!fresh.Any(r => HasRemarks(r, lineComments)))
        {
            await store.SetCopilotReviewHandledAsync(repositoryFullName, id, handledAt, cancellationToken);
            return new(CopilotReviewRevisionOutcome.NoRemarks, number);
        }

        var remarks = fresh.Sum(r => Remarks(r, lineComments));
        var round = reviews.Count(r => r.SubmittedAt <= since && HasRemarks(r, lineComments)) + 1;
        if (round > MaxRounds)
        {
            await store.SetCopilotReviewHandledAsync(repositoryFullName, id, handledAt, cancellationToken);
            return new(CopilotReviewRevisionOutcome.Skipped, number, remarks,
                $"this is round {round} of Copilot code review's remarks on pull request #{number}, and only {MaxRounds} are revised automatically. Start the next revision from the pull request.");
        }

        try
        {
            await workflow.StartRevisionRunAsync(repositoryFullName, id, cancellationToken: cancellationToken);
        }
        catch (DomainRuleException ex)
        {
            await store.SetCopilotReviewHandledAsync(repositoryFullName, id, handledAt, cancellationToken);
            return new(CopilotReviewRevisionOutcome.Skipped, number, remarks, ex.Message);
        }
        // Should this fail, the revision that just started still keeps the review from being acted on again: it ends after it.
        await store.SetCopilotReviewHandledAsync(repositoryFullName, id, handledAt, cancellationToken);
        return new(CopilotReviewRevisionOutcome.Started, number, remarks);
    }

    /// <summary>A review that requested changes or left line comments; the summary Copilot writes on every review is no remark by itself.</summary>
    private static bool HasRemarks(GitHubPullRequestReview review, IReadOnlyList<GitHubComment> lineComments)
    {
        return review.State == "CHANGES_REQUESTED" || Remarks(review, lineComments) > 0;
    }

    /// <summary>The line comments of the review that start a thread; replies in threads are part of the discussion, not remarks.</summary>
    private static int Remarks(GitHubPullRequestReview review, IReadOnlyList<GitHubComment> lineComments)
    {
        return lineComments.Count(c => c.PullRequestReviewId == review.Id && c.InReplyToId is null && !PullRequestDiscussion.IsFromWorker(c.Body));
    }
}

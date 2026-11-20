using System.Text;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Asks the item's agent by itself to revise its pull request once Copilot code review has reviewed it with remarks, for items
/// with <see cref="WorkItem.ReviseOnCopilotReview"/>: for the DGHSF agent worker it starts a revision run, which reads the
/// remarks from the pull request's discussion; for the Copilot cloud agent it posts the remarks as feedback mentioning
/// <c>@copilot</c> (<see cref="WorkItemPullRequests.SendFeedbackAsync"/>). Either way Copilot code review is asked to review
/// every revision (the worker does it itself, the sync does it for Copilot), so the review and revision rounds would go on as
/// long as Copilot finds something: only <see cref="MaxRounds"/> rounds of remarks are revised automatically, after that the
/// user starts the next revision.
/// </summary>
public sealed class CopilotReviewRevisions(IWorkItemStore store, IAgentRunStore agentRuns, GitHubApiClient gitHub, WorkItemWorkflow workflow,
    WorkItemPullRequests pullRequests)
{
    /// <summary>How many reviews with remarks of Copilot code review on one pull request are revised automatically.</summary>
    public const int MaxRounds = 3;

    /// <summary>The start of the feedback that asks the Copilot cloud agent to address Copilot code review's remarks.</summary>
    public const string CopilotRequest =
        "Copilot code review left remarks on this pull request. Please address each of them (or explain why one doesn't apply), "
        + "and make sure the code still builds and the tests still pass.";

    /// <summary>
    /// Asks for the revision when Copilot code review submitted a review with remarks (line comments, or requested changes)
    /// after the agent's last revision and after the review the sync acted on last. Every review is acted on once
    /// (<see cref="WorkItem.CopilotReviewHandledAt"/>), also when no revision could be asked for.
    /// </summary>
    /// <exception cref="GitHubApiException">GitHub refused the token, the rate limit was reached or GitHub had an error.</exception>
    public async Task<CopilotReviewRevisionResult> StartWhenReviewedAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
        if (item is not { ReviseOnCopilotReview: true, Status: WorkItemStatus.InProgress }
            || item.Progress is CopilotProgress.Merged or CopilotProgress.Closed)
        {
            return CopilotReviewRevisionResult.NotDue;
        }

        int number;
        DateTimeOffset since;
        if (item.Executor == AgentExecutor.DghsfWorker)
        {
            // While the worker runs, it isn't started again; a review that comes in meanwhile is part of the feedback of the next one.
            var runs = await agentRuns.GetForWorkItemAsync(repositoryFullName, id, cancellationToken);
            if (runs.Any(r => r.IsActive) || runs.FirstOrDefault(r => r.PullRequestNumber is not null)?.PullRequestNumber is not { } workerNumber)
            {
                return CopilotReviewRevisionResult.NotDue;
            }
            number = workerNumber;
            since = new[] { PullRequestDiscussion.HandledUntil(runs, number), item.CopilotReviewHandledAt ?? DateTimeOffset.MinValue }
                .Concat(runs.Select(r => r.CompletedAt ?? DateTimeOffset.MinValue))
                .Max();
        }
        else
        {
            // While Copilot works, or until the sync asked Copilot code review to review the revision Copilot was asked for,
            // the reviews on GitHub are about an earlier state of the pull request.
            if (item is not { PullRequestNumber: { } copilotNumber, CopilotRevisionRequestedAt: null }
                || item.Progress is CopilotProgress.Queued or CopilotProgress.Working)
            {
                return CopilotReviewRevisionResult.NotDue;
            }
            number = copilotNumber;
            since = item.CopilotReviewHandledAt ?? DateTimeOffset.MinValue;
        }

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

        string? warning = null;
        try
        {
            if (item.Executor == AgentExecutor.DghsfWorker)
            {
                await workflow.StartRevisionRunAsync(repositoryFullName, id, cancellationToken: cancellationToken);
            }
            else
            {
                var feedback = new PullRequestFeedback(repositoryFullName, id, CopilotFeedback(fresh, lineComments), []);
                warning = (await pullRequests.SendFeedbackAsync(feedback, cancellationToken)).Warning;
            }
        }
        catch (DomainRuleException ex)
        {
            await store.SetCopilotReviewHandledAsync(repositoryFullName, id, handledAt, cancellationToken);
            return new(CopilotReviewRevisionOutcome.Skipped, number, remarks, ex.Message);
        }
        // Should this fail, the revision that was just asked for still keeps the review from being acted on again: it ends after it.
        await store.SetCopilotReviewHandledAsync(repositoryFullName, id, handledAt, cancellationToken);
        return new(CopilotReviewRevisionOutcome.Started, number, remarks, warning);
    }

    /// <summary>
    /// The feedback for the Copilot cloud agent: <see cref="CopilotRequest"/> followed by every remark of the new reviews, with
    /// its file, line and link, so Copilot gets them however it reads the pull request. A review that requested changes
    /// without line comments is listed by its text.
    /// </summary>
    public static string CopilotFeedback(IReadOnlyList<GitHubPullRequestReview> reviews, IReadOnlyList<GitHubComment> lineComments)
    {
        var body = new StringBuilder(CopilotRequest);
        body.AppendLine().AppendLine();
        foreach (var review in reviews)
        {
            var comments = lineComments
                .Where(c => IsRemarkOf(review, c))
                .OrderBy(c => c.CreatedAt)
                .ToList();
            foreach (var comment in comments)
            {
                var line = comment.Line ?? comment.OriginalLine;
                body.AppendLine($"- `{comment.Path}`{(line is null ? string.Empty : $" line {line}")} ({comment.HtmlUrl}):");
                AppendQuote(body, comment.Body);
            }
            if (comments.Count == 0 && review.State == "CHANGES_REQUESTED")
            {
                body.AppendLine($"- The review {review.HtmlUrl} requested changes:");
                AppendQuote(body, review.Body);
            }
        }
        return body.ToString().TrimEnd();
    }

    private static void AppendQuote(StringBuilder body, string? text)
    {
        foreach (var line in (text ?? string.Empty).Trim().ReplaceLineEndings("\n").Split('\n'))
        {
            body.AppendLine($"  > {line}".TrimEnd());
        }
    }

    /// <summary>A review that requested changes or left line comments; the summary Copilot writes on every review is no remark by itself.</summary>
    private static bool HasRemarks(GitHubPullRequestReview review, IReadOnlyList<GitHubComment> lineComments)
    {
        return review.State == "CHANGES_REQUESTED" || Remarks(review, lineComments) > 0;
    }

    /// <summary>The line comments of the review that start a thread; replies in threads are part of the discussion, not remarks.</summary>
    private static int Remarks(GitHubPullRequestReview review, IReadOnlyList<GitHubComment> lineComments)
    {
        return lineComments.Count(c => IsRemarkOf(review, c));
    }

    private static bool IsRemarkOf(GitHubPullRequestReview review, GitHubComment comment)
    {
        return comment.PullRequestReviewId == review.Id && comment.InReplyToId is null && !PullRequestDiscussion.IsFromWorker(comment.Body);
    }
}

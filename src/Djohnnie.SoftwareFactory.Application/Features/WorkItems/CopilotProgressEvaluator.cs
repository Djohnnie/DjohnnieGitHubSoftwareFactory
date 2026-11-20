using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>Works out how far Copilot got from the issue, its linked pull request and Copilot's agent run; no GitHub requests.</summary>
public static class CopilotProgressEvaluator
{
    /// <summary>The prefix Copilot puts in its pull request's title while it works and removes when it finishes.</summary>
    public const string WorkInProgressPrefix = "[WIP]";

    /// <summary>The pull request the progress is about: the newest open one, else the newest merged one, else the newest closed one.</summary>
    public static GitHubLinkedPullRequest? SelectPullRequest(IReadOnlyList<GitHubLinkedPullRequest> pullRequests)
    {
        return pullRequests
            .OrderByDescending(pr => pr.IsOpen)
            .ThenByDescending(pr => pr.IsMerged)
            .ThenByDescending(pr => pr.UpdatedAt)
            .FirstOrDefault();
    }

    /// <param name="pullRequest">From <see cref="SelectPullRequest"/>.</param>
    /// <param name="run">Copilot's latest agent run on the pull request's branch, or <c>null</c> when unknown.</param>
    public static WorkItemProgressSnapshot Evaluate(bool issueClosed, bool assignedToCopilot, GitHubLinkedPullRequest? pullRequest, GitHubWorkflowRun? run)
    {
        if (pullRequest is null)
        {
            return new(!issueClosed && assignedToCopilot ? CopilotProgress.Queued : CopilotProgress.None);
        }

        var progress = pullRequest switch
        {
            { IsMerged: true } => CopilotProgress.Merged,
            { IsOpen: false } => CopilotProgress.Closed,
            _ => OpenProgress(pullRequest, run)
        };
        return new(progress, pullRequest.Number, pullRequest.Url, ChecksOf(pullRequest.ChecksState), run?.HtmlUrl, pullRequest.HeadRefName,
            ConflictsOf(pullRequest));
    }

    /// <summary>
    /// Whether the pull request's branch conflicts with its base branch: only an open one can, and <c>null</c> while GitHub
    /// is still computing it.
    /// </summary>
    public static bool? ConflictsOf(GitHubLinkedPullRequest pullRequest)
    {
        if (!pullRequest.IsOpen)
        {
            return false;
        }
        return pullRequest.Mergeable switch
        {
            "CONFLICTING" => true,
            "MERGEABLE" => false,
            _ => null
        };
    }

    /// <summary>
    /// How far the DGHSF agent worker got: its latest run while that hasn't succeeded, then the pull request it opened. A
    /// merged or closed pull request wins over a run that ended, and a run that succeeded shows its own pull request until
    /// GitHub links it to the issue.
    /// </summary>
    /// <param name="run">The item's latest run that wasn't cancelled, or <c>null</c> when it has none.</param>
    public static WorkItemProgressSnapshot EvaluateWorker(bool issueClosed, AgentRun? run, GitHubLinkedPullRequest? pullRequest)
    {
        if (run is null || (pullRequest is { IsOpen: false } && !run.IsActive))
        {
            return Evaluate(issueClosed, assignedToCopilot: false, pullRequest, run: null);
        }

        CopilotProgress? progress = run.Status switch
        {
            AgentRunStatus.Pending or AgentRunStatus.Queued => CopilotProgress.Queued,
            AgentRunStatus.Running => CopilotProgress.Working,
            AgentRunStatus.Failed => CopilotProgress.Stopped,
            _ => null
        };
        if (progress is { } working)
        {
            return pullRequest is null
                ? new(working)
                : new(working, pullRequest.Number, pullRequest.Url, ChecksOf(pullRequest.ChecksState), null, pullRequest.HeadRefName,
                    ConflictsOf(pullRequest));
        }
        return pullRequest is null
            ? new(CopilotProgress.ReadyForReview, run.PullRequestNumber, run.PullRequestUrl, PullRequestChecks.None, null, run.Branch)
            : Evaluate(issueClosed, assignedToCopilot: true, pullRequest, run: null);
    }

    private static CopilotProgress OpenProgress(GitHubLinkedPullRequest pullRequest, GitHubWorkflowRun? run)
    {
        if (run is not null)
        {
            if (!string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return CopilotProgress.Working;
            }
            if (run.Conclusion is "failure" or "cancelled" or "timed_out" or "startup_failure" or "action_required")
            {
                return CopilotProgress.Stopped;
            }
        }
        else if (pullRequest.Title.StartsWith(WorkInProgressPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Without the run (no access to Actions), the title is the only sign that Copilot is still busy.
            return CopilotProgress.Working;
        }

        // A requested Copilot code review is still coming, so earlier verdicts may be outdated: wait for it first.
        if (pullRequest.CopilotReviewRequested)
        {
            return CopilotProgress.CopilotReviewing;
        }

        // Without required reviews there is no review decision; then the reviewers' latest reviews decide.
        return pullRequest.ReviewDecision switch
        {
            "CHANGES_REQUESTED" => CopilotProgress.ChangesRequested,
            "APPROVED" => CopilotProgress.Approved,
            null when pullRequest.LatestReviewStates.Contains("CHANGES_REQUESTED") => CopilotProgress.ChangesRequested,
            null when pullRequest.LatestReviewStates.Contains("APPROVED") => CopilotProgress.Approved,
            _ => CopilotProgress.ReadyForReview
        };
    }

    public static PullRequestChecks ChecksOf(string? rollupState)
    {
        return rollupState switch
        {
            "SUCCESS" => PullRequestChecks.Passing,
            "FAILURE" or "ERROR" => PullRequestChecks.Failing,
            "PENDING" or "EXPECTED" => PullRequestChecks.Pending,
            _ => PullRequestChecks.None
        };
    }
}

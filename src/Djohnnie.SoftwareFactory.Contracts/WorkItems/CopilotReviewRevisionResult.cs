namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>What the sync did about a Copilot code review of the pull request of an item's agent (<see cref="WorkItem.ReviseOnCopilotReview"/>).</summary>
public enum CopilotReviewRevisionOutcome
{
    /// <summary>Nothing to do: the item doesn't revise on Copilot's reviews, the agent is busy or Copilot hasn't reviewed since the agent's last revision.</summary>
    NotDue = 0,

    /// <summary>Copilot code review reviewed the pull request without remarks, so there is nothing to revise.</summary>
    NoRemarks = 1,

    /// <summary>Copilot code review left remarks, so a revision run of the worker was started, or the remarks were sent to Copilot as feedback.</summary>
    Started = 2,

    /// <summary>Copilot code review left remarks, but the agent wasn't asked to revise; the reason says why.</summary>
    Skipped = 3
}

/// <param name="PullRequestNumber">The agent's pull request the review is about, when known.</param>
/// <param name="Remarks">The number of line comments the new reviews of Copilot code review left.</param>
/// <param name="Reason">Why the agent wasn't asked to revise, or what went wrong after it was; otherwise <c>null</c>.</param>
public sealed record CopilotReviewRevisionResult(CopilotReviewRevisionOutcome Outcome, int? PullRequestNumber = null, int Remarks = 0, string? Reason = null)
{
    public static CopilotReviewRevisionResult NotDue { get; } = new(CopilotReviewRevisionOutcome.NotDue);
}

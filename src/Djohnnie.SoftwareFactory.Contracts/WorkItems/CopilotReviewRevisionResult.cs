namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>What the sync did about a Copilot code review of a pull request of the DGHSF agent worker (<see cref="WorkItem.ReviseOnCopilotReview"/>).</summary>
public enum CopilotReviewRevisionOutcome
{
    /// <summary>Nothing to do: the item doesn't revise on Copilot's reviews, the worker is busy or Copilot hasn't reviewed since the worker's last run.</summary>
    NotDue = 0,

    /// <summary>Copilot code review reviewed the pull request without remarks, so there is nothing to revise.</summary>
    NoRemarks = 1,

    /// <summary>Copilot code review left remarks, so a revision run of the worker was started.</summary>
    Started = 2,

    /// <summary>Copilot code review left remarks, but no revision run was started; the reason says why.</summary>
    Skipped = 3
}

/// <param name="PullRequestNumber">The worker's pull request the review is about, when known.</param>
/// <param name="Remarks">The number of line comments the new reviews of Copilot code review left.</param>
/// <param name="Reason">Why no revision run was started; <c>null</c> for the other outcomes.</param>
public sealed record CopilotReviewRevisionResult(CopilotReviewRevisionOutcome Outcome, int? PullRequestNumber = null, int Remarks = 0, string? Reason = null)
{
    public static CopilotReviewRevisionResult NotDue { get; } = new(CopilotReviewRevisionOutcome.NotDue);
}

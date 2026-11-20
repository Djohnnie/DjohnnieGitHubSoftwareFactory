namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>What the sync did about the Copilot code review of a revision by the Copilot cloud agent.</summary>
public enum CopilotRevisionReviewOutcome
{
    /// <summary>No feedback was sent to Copilot that still waits for a review.</summary>
    NotDue = 0,

    /// <summary>Copilot hasn't started or finished its agent session for the feedback yet; the next sync checks again.</summary>
    Waiting = 1,

    /// <summary>Copilot finished, so Copilot code review was asked to review the pull request.</summary>
    Requested = 2,

    /// <summary>No review will be requested for this feedback, e.g. because the pull request was closed; the reason says why.</summary>
    Abandoned = 3
}

/// <param name="PullRequestNumber">The pull request the review is (or would have been) about, when known.</param>
/// <param name="Reason">Why the review was abandoned; <c>null</c> for the other outcomes.</param>
public sealed record CopilotRevisionReviewResult(CopilotRevisionReviewOutcome Outcome, int? PullRequestNumber = null, string? Reason = null)
{
    public static CopilotRevisionReviewResult NotDue { get; } = new(CopilotRevisionReviewOutcome.NotDue);
}

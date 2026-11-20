namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>What the sync did once an agent session of the Copilot cloud agent on the item's pull request had finished.</summary>
public enum CopilotRevisionReviewOutcome
{
    /// <summary>No finished agent session of Copilot waits to be handled (none, or the last one was handled already).</summary>
    NotDue = 0,

    /// <summary>Copilot hasn't started or finished its agent session for the feedback yet; the next sync checks again.</summary>
    Waiting = 1,

    /// <summary>Copilot finished, so Copilot code review was asked to review the pull request.</summary>
    Requested = 2,

    /// <summary>No review will be requested for this feedback, e.g. because the pull request was closed; the reason says why.</summary>
    Abandoned = 3,

    /// <summary>
    /// Copilot finished, but Copilot code review was already asked to review the pull request or reviewed it after the
    /// session (e.g. by a ruleset), so it wasn't asked again.
    /// </summary>
    AlreadyReviewed = 4
}

/// <param name="PullRequestNumber">The pull request the review is (or would have been) about, when known.</param>
/// <param name="Reason">
/// Why the review was abandoned; for the other outcomes what went wrong along the way (e.g. the draft couldn't be marked ready
/// for review), or <c>null</c>.
/// </param>
/// <param name="MarkedReadyForReview">The pull request was a draft, and the sync marked it ready for review.</param>
/// <param name="AfterFeedback">The session was Copilot's revision for feedback sent from DGHSF, rather than any other session.</param>
public sealed record CopilotRevisionReviewResult(CopilotRevisionReviewOutcome Outcome, int? PullRequestNumber = null, string? Reason = null,
    bool MarkedReadyForReview = false, bool AfterFeedback = false)
{
    public static CopilotRevisionReviewResult NotDue { get; } = new(CopilotRevisionReviewOutcome.NotDue);
}

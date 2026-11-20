namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>What the sync did about merge conflicts of a work item's pull request (<see cref="WorkItem.FixMergeConflicts"/>).</summary>
public enum MergeConflictFixOutcome
{
    /// <summary>
    /// Nothing to do: the pull request doesn't conflict, the item doesn't fix conflicts by itself, the agent was asked for this
    /// conflict already or the agent is still working.
    /// </summary>
    NotDue = 0,

    /// <summary>The agent was asked to fix the conflicts with a comment in the pull request's discussion.</summary>
    Requested = 1,

    /// <summary>The pull request conflicts, but the agent couldn't be asked; the reason says why. It isn't retried for this conflict.</summary>
    Skipped = 2
}

/// <param name="PullRequestNumber">The conflicting pull request, when known.</param>
/// <param name="AgentRunStarted">The DGHSF agent worker starts a revision run on the request.</param>
/// <param name="Reason">Why the agent wasn't asked, or why no revision run started although it was; <c>null</c> otherwise.</param>
public sealed record MergeConflictFixResult(MergeConflictFixOutcome Outcome, int? PullRequestNumber = null, bool AgentRunStarted = false, string? Reason = null)
{
    public static MergeConflictFixResult NotDue { get; } = new(MergeConflictFixOutcome.NotDue);
}

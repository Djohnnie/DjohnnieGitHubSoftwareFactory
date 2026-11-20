namespace Djohnnie.SoftwareFactory.Contracts.SyncLog;

/// <summary>What a sync log event is about.</summary>
public enum SyncLogEventKind
{
    /// <summary>The summary at the end of a run that changed something or had problems.</summary>
    RunCompleted = 0,

    /// <summary>
    /// Runs that found no work items with a GitHub issue. Consecutive ones share one event, whose
    /// <see cref="SyncLogEvent.Iterations"/> counts them.
    /// </summary>
    NoEligibleWorkItems = 1,

    /// <summary>Runs that checked work items without finding a change or a problem; consecutive ones share one event.</summary>
    NoChanges = 2,

    /// <summary>A work item took over something from its GitHub issue or pull request.</summary>
    WorkItemChanged = 3,

    /// <summary>A work item could not be synced; the others were.</summary>
    WorkItemFailed = 4,

    /// <summary>
    /// The work items of a repository could not be listed, so none of them were synced, or the sync could not check whether
    /// the repository still exists on GitHub.
    /// </summary>
    RepositoryFailed = 5,

    /// <summary>A work item was synced, but Copilot's progress could not be read from its pull request or agent run.</summary>
    ProgressUnavailable = 6,

    /// <summary>The run stopped early because GitHub refused the token or the rate limit was reached.</summary>
    RunStopped = 7,

    /// <summary>The run failed before it could sync anything, or was cancelled.</summary>
    RunFailed = 8,

    /// <summary>
    /// GitHub no longer knows a repository, so it was archived: left out of the favorites and the recently accessed
    /// repositories and of the next syncs, while what DGHSF stored can still be opened.
    /// </summary>
    RepositoryArchived = 9,

    /// <summary>An archived repository exists on GitHub again, so it is synced again.</summary>
    RepositoryRestored = 10,

    /// <summary>The Copilot cloud agent finished a revision asked from DGHSF, so Copilot code review was asked to review the pull request.</summary>
    CopilotReviewRequested = 11,

    /// <summary>
    /// No Copilot code review will be requested for a revision asked from the Copilot cloud agent, e.g. because the pull request
    /// was closed, Copilot's session failed or GitHub refused the request.
    /// </summary>
    CopilotReviewAbandoned = 12,

    /// <summary>
    /// Copilot code review left remarks on an agent's pull request, so the sync asked the agent to revise it: a revision run
    /// of the DGHSF agent worker, or feedback for the Copilot cloud agent (<see cref="WorkItems.WorkItem.ReviseOnCopilotReview"/>).
    /// </summary>
    CopilotReviewRevisionStarted = 13,

    /// <summary>
    /// Copilot code review left remarks on an agent's pull request, but the sync didn't ask the agent to revise it,
    /// e.g. because enough rounds were revised automatically already or the revision was refused.
    /// </summary>
    CopilotReviewRevisionSkipped = 14,

    /// <summary>
    /// A work item's pull request conflicts with its base branch, so the sync asked its agent to fix it with a comment in the
    /// pull request's discussion (<see cref="WorkItems.WorkItem.FixMergeConflicts"/>).
    /// </summary>
    MergeConflictFixRequested = 15,

    /// <summary>A work item's pull request conflicts with its base branch, but the sync couldn't ask its agent to fix it.</summary>
    MergeConflictFixSkipped = 16
}

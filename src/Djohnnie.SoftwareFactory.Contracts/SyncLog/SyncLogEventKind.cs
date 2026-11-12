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

    /// <summary>The work items of a repository could not be listed, so none of them were synced.</summary>
    RepositoryFailed = 5,

    /// <summary>A work item was synced, but Copilot's progress could not be read from its pull request or agent run.</summary>
    ProgressUnavailable = 6,

    /// <summary>The run stopped early because GitHub refused the token or the rate limit was reached.</summary>
    RunStopped = 7,

    /// <summary>The run failed before it could sync anything, or was cancelled.</summary>
    RunFailed = 8
}

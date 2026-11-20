namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>Stored as a number: never renumber the values.</summary>
public enum WorkItemStatus
{
    /// <summary>Only in the app's database.</summary>
    Draft = 0,

    /// <summary>A GitHub issue was created.</summary>
    Accepted = 1,

    /// <summary>The Copilot cloud agent was assigned to the issue.</summary>
    InProgress = 2,

    /// <summary>The issue was closed and/or the pull request merged.</summary>
    Done = 3,

    /// <summary>
    /// A draft that the background agents generated from the repository ("Generate from repository"); nobody wrote it.
    /// It behaves exactly like <see cref="Draft"/>: it can be edited, accepted and deleted.
    /// </summary>
    Generated = 4,

    /// <summary>
    /// The issue was closed without the work being done: as "not planned" or as a duplicate. It doesn't count as finished
    /// work (in the counts, the map's progress and the linked mindmap ideas), and no agent works on it any more.
    /// </summary>
    NotPlanned = 5
}

public static class WorkItemStatuses
{
    extension(WorkItemStatus status)
    {
        /// <summary>Only in the app's database, without a GitHub issue: <see cref="WorkItemStatus.Draft"/> or <see cref="WorkItemStatus.Generated"/>.</summary>
        public bool IsDraft
        {
            get
            {
                return status is WorkItemStatus.Draft or WorkItemStatus.Generated;
            }
        }

        /// <summary>The GitHub issue is closed, done or not: <see cref="WorkItemStatus.Done"/> or <see cref="WorkItemStatus.NotPlanned"/>.</summary>
        public bool IsClosed
        {
            get
            {
                return status is WorkItemStatus.Done or WorkItemStatus.NotPlanned;
            }
        }
    }
}

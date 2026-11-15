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
    Generated = 4
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
    }
}

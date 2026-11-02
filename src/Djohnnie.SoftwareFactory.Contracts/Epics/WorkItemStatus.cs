namespace Djohnnie.SoftwareFactory.Contracts.Epics;

public enum WorkItemStatus
{
    /// <summary>Only in the app's database.</summary>
    Draft = 0,

    /// <summary>A GitHub issue was created.</summary>
    Accepted = 1,

    /// <summary>The Copilot cloud agent was assigned to the issue.</summary>
    InProgress = 2,

    /// <summary>The issue was closed and/or the pull request merged.</summary>
    Done = 3
}


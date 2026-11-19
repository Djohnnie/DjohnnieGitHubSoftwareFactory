namespace Djohnnie.SoftwareFactory.Contracts.AgentRuns;

/// <summary>What a task of the DGHSF agent worker does; stored by name.</summary>
public enum AgentRunKind
{
    /// <summary>Implements the work item on a new branch from the base branch and opens a pull request.</summary>
    Implement = 0,

    /// <summary>
    /// Continues the pull request an earlier task opened: merges the base branch into its branch, addresses the feedback on
    /// it since the agent's last run, pushes to the same branch and answers the feedback.
    /// </summary>
    Revise = 1
}

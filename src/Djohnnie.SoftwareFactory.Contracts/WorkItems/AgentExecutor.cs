namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>Where the coding agent of a user story, bug or task runs. Stored as a number: never renumber the values.</summary>
public enum AgentExecutor
{
    /// <summary>GitHub's Copilot cloud agent: the issue is assigned to Copilot and GitHub runs the agent.</summary>
    CopilotCloudAgent = 0,

    /// <summary>
    /// The DGHSF agent worker: a container job runs the agent with the GitHub Copilot SDK on a clone of the repository and
    /// opens the pull request itself (see <see cref="AgentRuns.AgentRun"/>).
    /// </summary>
    DghsfWorker = 1
}

public static class AgentExecutors
{
    /// <summary>The executors in the order they are offered: the DGHSF agent worker (the default for new items) first.</summary>
    public static IReadOnlyList<AgentExecutor> All { get; } = [AgentExecutor.DghsfWorker, AgentExecutor.CopilotCloudAgent];

    extension(AgentExecutor executor)
    {
        public string DisplayName
        {
            get
            {
                return executor switch
                {
                    AgentExecutor.DghsfWorker => "DGHSF agent worker",
                    _ => "Copilot cloud agent"
                };
            }
        }

        public string Description
        {
            get
            {
                return executor switch
                {
                    AgentExecutor.DghsfWorker => "DGHSF runs the agent with the Copilot SDK in its own container job and tracks the AI credits of every run.",
                    _ => "The issue is assigned to Copilot and GitHub runs the agent."
                };
            }
        }
    }
}

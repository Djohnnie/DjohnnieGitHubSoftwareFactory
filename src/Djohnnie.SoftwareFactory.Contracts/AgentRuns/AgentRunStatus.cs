namespace Djohnnie.SoftwareFactory.Contracts.AgentRuns;

/// <summary>Where a run of the DGHSF agent worker is in its life cycle; stored by name.</summary>
public enum AgentRunStatus
{
    /// <summary>Waits for the dispatcher to put it on the Service Bus queue.</summary>
    Pending = 0,

    /// <summary>On the queue; no worker has picked it up yet.</summary>
    Queued = 1,

    /// <summary>A worker is running the agent, as long as it keeps renewing its lease.</summary>
    Running = 2,

    /// <summary>The agent finished and the worker opened the pull request.</summary>
    Succeeded = 3,

    Failed = 4,
    Cancelled = 5
}

public static class AgentRunStatuses
{
    extension(AgentRunStatus status)
    {
        /// <summary>The statuses a run never leaves.</summary>
        public bool IsFinal
        {
            get
            {
                return status is AgentRunStatus.Succeeded or AgentRunStatus.Failed or AgentRunStatus.Cancelled;
            }
        }

        public string DisplayName
        {
            get
            {
                return status switch
                {
                    AgentRunStatus.Pending => "Waiting",
                    AgentRunStatus.Queued => "Queued",
                    AgentRunStatus.Running => "Running",
                    AgentRunStatus.Succeeded => "Succeeded",
                    AgentRunStatus.Failed => "Failed",
                    _ => "Cancelled"
                };
            }
        }
    }
}

namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>
/// How far the Copilot cloud agent got with a work item, from its pull request and the agent's Actions run.
/// Only user stories, bugs and tasks (the types that are assigned to an agent) get anything other than <see cref="None"/>.
/// </summary>
public enum CopilotProgress
{
    /// <summary>Nothing to show: Copilot isn't assigned and there is no pull request.</summary>
    None = 0,

    /// <summary>Copilot is assigned but hasn't opened a pull request yet.</summary>
    Queued = 1,

    /// <summary>Copilot's agent session is running (or queued) on the pull request.</summary>
    Working = 2,

    /// <summary>Copilot's last agent session failed, was cancelled or timed out.</summary>
    Stopped = 3,

    /// <summary>Copilot finished; the pull request waits for a review.</summary>
    ReadyForReview = 4,

    /// <summary>A reviewer asked for changes and Copilot isn't working on them (yet).</summary>
    ChangesRequested = 5,

    /// <summary>The pull request was approved but isn't merged yet.</summary>
    Approved = 6,

    /// <summary>The pull request was merged.</summary>
    Merged = 7,

    /// <summary>The pull request was closed without merging.</summary>
    Closed = 8
}

/// <summary>The combined state of the checks on the pull request's last commit.</summary>
public enum PullRequestChecks
{
    None = 0,
    Pending = 1,
    Passing = 2,
    Failing = 3
}

/// <summary>What a work item stores about Copilot's work on it when it is refreshed.</summary>
/// <param name="AgentRunUrl">The Actions run of Copilot's latest agent session, when the token may read Actions.</param>
/// <param name="PullRequestBranch">The pull request's head branch, where Copilot pushes its commits.</param>
public sealed record WorkItemProgressSnapshot(
    CopilotProgress Progress,
    int? PullRequestNumber = null,
    string? PullRequestUrl = null,
    PullRequestChecks Checks = PullRequestChecks.None,
    string? AgentRunUrl = null,
    string? PullRequestBranch = null)
{
    public static WorkItemProgressSnapshot None { get; } = new(CopilotProgress.None);
}

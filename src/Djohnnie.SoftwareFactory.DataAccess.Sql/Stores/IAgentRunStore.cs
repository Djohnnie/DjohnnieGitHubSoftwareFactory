using Djohnnie.SoftwareFactory.Contracts.AgentRuns;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

/// <summary>
/// The runs of the DGHSF agent worker. Every status change is a conditional update on the expected status, so a run is
/// claimed by one dispatcher and started by one worker, also when Service Bus delivers a message twice.
/// </summary>
public interface IAgentRunStore
{
    /// <summary>
    /// Starts a <see cref="AgentRunStatus.Pending"/> run for a work item; the dispatcher picks it up. The item has one task:
    /// the first call adds it, later calls reopen it with <paramref name="maxAttempts"/> more runs, so every run on the item
    /// is a child of the same task. Refuses when the item already has a run that hasn't ended.
    /// </summary>
    Task<AgentRun> CreateAsync(string repositoryFullName, Guid workItemId, string? requestedModel, int maxAttempts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a <see cref="AgentRunStatus.Pending"/> <see cref="AgentRunKind.Revise"/> run for the pull request on
    /// <paramref name="branch"/> as a run of the item's task, like <see cref="CreateAsync"/>, and refuses like it.
    /// </summary>
    Task<AgentRun> CreateRevisionAsync(string repositoryFullName, Guid workItemId, string? requestedModel, int maxAttempts, string branch,
        int pullRequestNumber, string pullRequestUrl, CancellationToken cancellationToken = default);

    /// <summary>Marks up to <paramref name="max"/> pending runs as queued, oldest first, and returns their ids to send.</summary>
    Task<IReadOnlyList<Guid>> ClaimDueAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes back runs that got stuck: queued longer than <paramref name="queuedTimeout"/> (the message was lost) or running
    /// past their lease (the worker stopped). A run without attempts left fails. Returns how many runs changed.
    /// </summary>
    Task<int> RecoverAsync(TimeSpan queuedTimeout, CancellationToken cancellationToken = default);

    /// <summary>Starts a queued run for <paramref name="lease"/>; <c>null</c> when it isn't queued (a duplicate message, or cancelled).</summary>
    Task<AgentRun?> TryStartAsync(Guid id, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>Extends the lease of a running run; <c>false</c> when it is no longer running (cancelled or taken back).</summary>
    Task<bool> RenewLeaseAsync(Guid id, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends a running run with its pull request and how far it handled the pull request's discussion; <c>false</c> when it
    /// was no longer running.
    /// </summary>
    Task<bool> CompleteAsync(Guid id, AgentRunResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an error of a running run, with what the agent cost so far. With <paramref name="retry"/> and attempts left
    /// it is pending again; otherwise it fails. Returns the new status, or <c>null</c> when it wasn't running.
    /// </summary>
    Task<AgentRunStatus?> FailAsync(Guid id, string error, AgentRunUsage? usage, bool retry, CancellationToken cancellationToken = default);

    /// <summary>Cancels a run that hasn't ended; a worker that is running it stops when it next renews its lease.</summary>
    Task<bool> CancelAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default);

    Task<AgentRun?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The runs of a work item, newest first.</summary>
    Task<List<AgentRun>> GetForWorkItemAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default);

    /// <summary>The tasks of a work item, newest first, each with the item and its runs (one per attempt, newest first).</summary>
    Task<List<AgentRunOverview>> GetOverviewsForWorkItemAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default);

    /// <summary>The newest tasks of a repository, newest first, each with its work item and its runs (one per attempt, newest first).</summary>
    Task<List<AgentRunOverview>> GetForRepositoryAsync(string repositoryFullName, int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest task of a work item that wasn't cancelled, or was cancelled after it opened a pull request; otherwise <c>null</c>.
    /// </summary>
    Task<AgentRun?> GetLatestAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default);
}

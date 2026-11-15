using Djohnnie.SoftwareFactory.Contracts.TodoTasks;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

/// <summary>
/// The background tasks the Tasks function app executes. Every status change is a conditional update on the expected status,
/// so a task is claimed by one dispatcher and started by one executor, also when messages are delivered twice.
/// </summary>
public interface ITodoTaskStore
{
    /// <summary>Adds the root task of a new run as <see cref="TodoTaskStatus.Pending"/>; the dispatcher picks it up.</summary>
    Task<TodoTask> CreateRunAsync(string repositoryFullName, NewTodoTask task, string? createdBy, int maxAttempts, CancellationToken cancellationToken = default);

    /// <summary>Whether the repository has a run of this kind that hasn't ended.</summary>
    Task<bool> HasActiveRunAsync(string repositoryFullName, TodoTaskKind kind, CancellationToken cancellationToken = default);

    /// <summary>Marks up to <paramref name="max"/> due pending tasks as queued, oldest first, and returns their ids to send.</summary>
    Task<IReadOnlyList<Guid>> ClaimDueAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes back tasks that got stuck: queued longer than <paramref name="queuedTimeout"/> (the message was lost) or
    /// running past their lease (the executor stopped). A task without attempts left fails. Also ends waiting tasks whose
    /// sub-tasks have all ended. Returns how many tasks changed.
    /// </summary>
    Task<int> RecoverAsync(TimeSpan queuedTimeout, CancellationToken cancellationToken = default);

    /// <summary>Starts a queued task for <paramref name="lease"/>; <c>null</c> when it isn't queued (a duplicate message, or cancelled).</summary>
    Task<TodoTask?> TryStartAsync(Guid id, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends a running task's own work. Without sub-tasks it succeeds; with sub-tasks it waits for them and ends with the
    /// last one. <c>false</c> when the task is no longer running (cancelled or taken back meanwhile); nothing is stored then.
    /// </summary>
    Task<bool> CompleteAsync(Guid id, string? result, IReadOnlyList<NewTodoTask> subTasks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an error of a running task. It is retried from <paramref name="retryAt"/> while it has attempts left;
    /// otherwise (or without <paramref name="retryAt"/>) it fails. Returns the new status, or <c>null</c> when it wasn't running.
    /// </summary>
    Task<TodoTaskStatus?> FailAsync(Guid id, string error, DateTimeOffset? retryAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels every task of the run that hasn't ended; a task that is running finishes, but its result is discarded.</summary>
    Task<int> CancelRunAsync(string repositoryFullName, Guid rootId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a run that has ended (its root task succeeded, failed or was cancelled) with all its sub-tasks. Returns
    /// <c>false</c> when the run doesn't exist or hasn't ended. The work items it generated are kept.
    /// </summary>
    Task<bool> DeleteRunAsync(string repositoryFullName, Guid rootId, CancellationToken cancellationToken = default);

    /// <summary>The newest runs of a repository, newest first, with their sub-tasks.</summary>
    Task<List<TodoTaskRun>> GetRunsAsync(string repositoryFullName, int max, CancellationToken cancellationToken = default);

    Task<TodoTaskRun?> GetRunAsync(string repositoryFullName, Guid rootId, CancellationToken cancellationToken = default);

    /// <summary>The status of a task, or <c>null</c> when it doesn't exist; handlers check it between expensive steps.</summary>
    Task<TodoTaskStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);
}

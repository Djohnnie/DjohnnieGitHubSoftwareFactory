namespace Djohnnie.SoftwareFactory.Contracts.TodoTasks;

/// <summary>Where a TODO task is in its life cycle; stored by name.</summary>
public enum TodoTaskStatus
{
    /// <summary>Waits for the dispatcher to put it on the queue (from <see cref="TodoTask.NotBefore"/>, when set).</summary>
    Pending = 0,

    /// <summary>On the Service Bus queue; the executor hasn't picked it up yet.</summary>
    Queued = 1,

    /// <summary>An executor is working on it, until its lease ends.</summary>
    Running = 2,

    /// <summary>Its own work is done; it waits for the sub-tasks it created.</summary>
    Waiting = 3,

    Succeeded = 4,
    Failed = 5,
    Cancelled = 6
}

public static class TodoTaskStatuses
{
    /// <summary>The statuses a task never leaves.</summary>
    public static IReadOnlyList<TodoTaskStatus> Final { get; } = [TodoTaskStatus.Succeeded, TodoTaskStatus.Failed, TodoTaskStatus.Cancelled];

    extension(TodoTaskStatus status)
    {
        public bool IsFinal
        {
            get
            {
                return status is TodoTaskStatus.Succeeded or TodoTaskStatus.Failed or TodoTaskStatus.Cancelled;
            }
        }
    }
}

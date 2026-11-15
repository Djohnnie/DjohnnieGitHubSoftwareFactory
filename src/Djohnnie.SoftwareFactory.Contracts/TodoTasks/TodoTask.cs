namespace Djohnnie.SoftwareFactory.Contracts.TodoTasks;

/// <summary>
/// A unit of background work, stored in SQL and executed by the Tasks function app. A run is a tree of tasks: the root
/// task (<see cref="RootId"/> is its own id) creates sub-tasks, and it ends when all of them have ended.
/// </summary>
public sealed record TodoTask
{
    public const int MaxTitleLength = 300;
    public const int MaxErrorLength = 4_000;
    public const int MaxResultLength = 4_000;

    public Guid Id { get; init; }

    /// <summary>The root task of the run; its own id for a root task.</summary>
    public Guid RootId { get; init; }

    /// <summary>The task that created this one, or <c>null</c> for a root task.</summary>
    public Guid? ParentId { get; init; }

    public TodoTaskKind Kind { get; init; }
    public TodoTaskStatus Status { get; init; }

    /// <summary>The repository the task works on, as <c>owner/name</c> (lower case).</summary>
    public string RepositoryFullName { get; init; } = string.Empty;

    /// <summary>What the task does, for people.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The input of the task's handler as JSON.</summary>
    public string Payload { get; init; } = "{}";

    /// <summary>What the task did, for people; <c>null</c> until it ends (or when it had nothing to say).</summary>
    public string? Result { get; init; }

    /// <summary>The last error; kept when a retry succeeds, so problems stay visible.</summary>
    public string? Error { get; init; }

    /// <summary>How many times an executor started the task.</summary>
    public int Attempts { get; init; }

    public int MaxAttempts { get; init; }

    /// <summary>The task isn't queued before this moment (a retry's backoff), or <c>null</c>.</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>The GitHub login of the person who started the run, if known.</summary>
    public string? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? QueuedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>A task to create: a run's root task, or a sub-task returned by a handler.</summary>
public sealed record NewTodoTask(TodoTaskKind Kind, string Title, string Payload);

/// <summary>A run: its root task and all its sub-tasks, in the order they were created.</summary>
public sealed record TodoTaskRun(TodoTask Root, IReadOnlyList<TodoTask> SubTasks)
{
    public int Total
    {
        get
        {
            return SubTasks.Count;
        }
    }

    public int Succeeded
    {
        get
        {
            return SubTasks.Count(t => t.Status == TodoTaskStatus.Succeeded);
        }
    }

    public int Failed
    {
        get
        {
            return SubTasks.Count(t => t.Status == TodoTaskStatus.Failed);
        }
    }

    public bool IsActive
    {
        get
        {
            return !Root.Status.IsFinal;
        }
    }
}

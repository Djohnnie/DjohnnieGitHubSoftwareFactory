namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

/// <summary>How the Tasks function app schedules and retries TODO tasks (section <c>TodoTasks</c>).</summary>
public sealed class TodoTaskOptions
{
    public const string SectionName = "TodoTasks";

    /// <summary>
    /// How long an executor owns a started task; after that the dispatcher takes it back. Keep it above the function
    /// timeout, so a task that is still running is never started a second time.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(35);

    /// <summary>How long a task may stay queued before the dispatcher assumes its message was lost and queues it again.</summary>
    public TimeSpan QueuedTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How many times a task is started before it fails for good.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>How many tasks one dispatcher run queues at most.</summary>
    public int DispatchBatchSize { get; set; } = 50;

    /// <summary>The wait before the first retry; it doubles with every attempt.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}

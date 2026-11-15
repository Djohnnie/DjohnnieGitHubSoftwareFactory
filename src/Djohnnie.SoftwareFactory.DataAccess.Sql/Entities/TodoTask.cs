using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// A unit of background work that the Tasks function app executes; see <see cref="Contracts.TodoTasks.TodoTask"/>.
/// Status changes are conditional updates (<c>WHERE Status = expected</c>), so concurrent dispatchers and executors
/// can't both claim a task. Refers to no other table, like the sync log.
/// </summary>
public class TodoTask : Entity
{
    public Guid RootId { get; set; }
    public Guid? ParentId { get; set; }
    public TodoTaskKind Kind { get; set; }
    public TodoTaskStatus Status { get; set; }

    [Required, MaxLength(Repository.MaxFullNameLength)]
    public string RepositoryFullName { get; set; } = string.Empty;

    [Required, MaxLength(Contracts.TodoTasks.TodoTask.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Payload { get; set; } = "{}";

    [MaxLength(Contracts.TodoTasks.TodoTask.MaxResultLength)]
    public string? Result { get; set; }

    [MaxLength(Contracts.TodoTasks.TodoTask.MaxErrorLength)]
    public string? Error { get; set; }

    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public DateTimeOffset? NotBefore { get; set; }

    /// <summary>Until when the executor that is running the task owns it; after that the dispatcher takes it back.</summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

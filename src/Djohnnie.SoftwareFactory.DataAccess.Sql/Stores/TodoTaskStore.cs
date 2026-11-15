using System.Linq.Expressions;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class TodoTaskStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : ITodoTaskStore
{
    private static readonly Expression<Func<Entities.TodoTask, bool>> NotFinal = t =>
        t.Status != TodoTaskStatus.Succeeded && t.Status != TodoTaskStatus.Failed && t.Status != TodoTaskStatus.Cancelled;

    public async Task<TodoTask> CreateRunAsync(string repositoryFullName, NewTodoTask task, string? createdBy, int maxAttempts, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var id = Guid.NewGuid();
        var entity = New(task, id, id, null, Entities.Repository.Normalize(repositoryFullName), Math.Max(1, maxAttempts), timeProvider.GetUtcNow());
        entity.CreatedBy = Truncate(createdBy, 100);
        db.TodoTasks.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToContract();
    }

    public async Task<bool> HasActiveRunAsync(string repositoryFullName, TodoTaskKind kind, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        return await db.TodoTasks.Where(NotFinal)
            .AnyAsync(t => t.ParentId == null && t.RepositoryFullName == normalized && t.Kind == kind, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ClaimDueAsync(int max, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var candidates = await db.TodoTasks
            .Where(t => t.Status == TodoTaskStatus.Pending && (t.NotBefore == null || t.NotBefore <= now))
            .OrderBy(t => t.SysId)
            .Select(t => t.Id)
            .Take(max)
            .ToListAsync(cancellationToken);

        var claimed = new List<Guid>();
        foreach (var id in candidates)
        {
            var updated = await db.TodoTasks
                .Where(t => t.Id == id && t.Status == TodoTaskStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, TodoTaskStatus.Queued)
                    .SetProperty(t => t.QueuedAt, now), cancellationToken);
            if (updated == 1)
            {
                claimed.Add(id);
            }
        }
        return claimed;
    }

    public async Task<int> RecoverAsync(TimeSpan queuedTimeout, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var queuedBefore = now - queuedTimeout;

        // The dispatcher marked these queued, but the message never arrived (or was lost): queue them again.
        var changed = await db.TodoTasks
            .Where(t => t.Status == TodoTaskStatus.Queued && t.QueuedAt < queuedBefore)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TodoTaskStatus.Pending), cancellationToken);

        var expired = await db.TodoTasks.AsNoTracking()
            .Where(t => t.Status == TodoTaskStatus.Running && t.LeaseUntil < now)
            .Select(t => new { t.Id, t.ParentId, t.Attempts, t.MaxAttempts })
            .ToListAsync(cancellationToken);
        foreach (var task in expired)
        {
            var retry = task.Attempts < task.MaxAttempts;
            var error = $"The executor stopped before the task finished (attempt {task.Attempts} of {task.MaxAttempts}).";
            var updated = await db.TodoTasks
                .Where(t => t.Id == task.Id && t.Status == TodoTaskStatus.Running && t.LeaseUntil < now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, retry ? TodoTaskStatus.Pending : TodoTaskStatus.Failed)
                    .SetProperty(t => t.Error, error)
                    .SetProperty(t => t.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(t => t.CompletedAt, retry ? null : now), cancellationToken);
            changed += updated;
            if (updated == 1 && !retry)
            {
                changed += await EndAncestorsAsync(db, task.ParentId, now, cancellationToken);
            }
        }

        // Normally the last sub-task ends its parent; this catches a parent whose last sub-task ended in a crash.
        var waiting = await db.TodoTasks.AsNoTracking()
            .Where(t => t.Status == TodoTaskStatus.Waiting && !db.TodoTasks.Any(c => c.ParentId == t.Id
                && c.Status != TodoTaskStatus.Succeeded && c.Status != TodoTaskStatus.Failed && c.Status != TodoTaskStatus.Cancelled))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in waiting)
        {
            changed += await EndAncestorsAsync(db, id, now, cancellationToken);
        }
        return changed;
    }

    public async Task<TodoTask?> TryStartAsync(Guid id, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + lease;
        var updated = await db.TodoTasks
            .Where(t => t.Id == id && t.Status == TodoTaskStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TodoTaskStatus.Running)
                .SetProperty(t => t.StartedAt, now)
                .SetProperty(t => t.LeaseUntil, leaseUntil)
                .SetProperty(t => t.Attempts, t => t.Attempts + 1), cancellationToken);
        if (updated == 0)
        {
            return null;
        }

        var entity = await db.TodoTasks.AsNoTracking().SingleAsync(t => t.Id == id, cancellationToken);
        return entity.ToContract();
    }

    public async Task<bool> CompleteAsync(Guid id, string? result, IReadOnlyList<NewTodoTask> subTasks, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var status = subTasks.Count > 0 ? TodoTaskStatus.Waiting : TodoTaskStatus.Succeeded;
        DateTimeOffset? completedAt = status == TodoTaskStatus.Succeeded ? now : null;
        var text = Truncate(result, TodoTask.MaxResultLength);
        Guid? parentId;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var task = await db.TodoTasks.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == id && t.Status == TodoTaskStatus.Running, cancellationToken);
            if (task is null)
            {
                return false;
            }

            var updated = await db.TodoTasks
                .Where(t => t.Id == id && t.Status == TodoTaskStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, status)
                    .SetProperty(t => t.Result, text)
                    .SetProperty(t => t.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(t => t.CompletedAt, completedAt), cancellationToken);
            if (updated == 0)
            {
                return false;
            }

            db.TodoTasks.AddRange(subTasks.Select(sub => New(sub, Guid.NewGuid(), task.RootId, task.Id, task.RepositoryFullName, task.MaxAttempts, now)));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            parentId = task.ParentId;
        }

        if (status == TodoTaskStatus.Succeeded)
        {
            await EndAncestorsAsync(db, parentId, now, cancellationToken);
        }
        return true;
    }

    public async Task<TodoTaskStatus?> FailAsync(Guid id, string error, DateTimeOffset? retryAt, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var task = await db.TodoTasks.AsNoTracking()
            .Where(t => t.Id == id && t.Status == TodoTaskStatus.Running)
            .Select(t => new { t.ParentId, t.Attempts, t.MaxAttempts })
            .SingleOrDefaultAsync(cancellationToken);
        if (task is null)
        {
            return null;
        }

        var retry = retryAt is not null && task.Attempts < task.MaxAttempts;
        var status = retry ? TodoTaskStatus.Pending : TodoTaskStatus.Failed;
        DateTimeOffset? notBefore = retry ? retryAt : null;
        DateTimeOffset? completedAt = retry ? null : now;
        var text = Truncate(error, TodoTask.MaxErrorLength);
        var updated = await db.TodoTasks
            .Where(t => t.Id == id && t.Status == TodoTaskStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, status)
                .SetProperty(t => t.Error, text)
                .SetProperty(t => t.NotBefore, notBefore)
                .SetProperty(t => t.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(t => t.CompletedAt, completedAt), cancellationToken);
        if (updated == 0)
        {
            return null;
        }
        if (!retry)
        {
            await EndAncestorsAsync(db, task.ParentId, now, cancellationToken);
        }
        return status;
    }

    public async Task<int> CancelRunAsync(string repositoryFullName, Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        return await db.TodoTasks
            .Where(t => t.RootId == rootId && t.RepositoryFullName == normalized)
            .Where(NotFinal)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TodoTaskStatus.Cancelled)
                .SetProperty(t => t.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(t => t.CompletedAt, now), cancellationToken);
    }

    public async Task<bool> DeleteRunAsync(string repositoryFullName, Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        // Every task of the run must have ended. Ended tasks never change again, so nothing can start between this check
        // and the delete.
        var tasks = db.TodoTasks.Where(t => t.RootId == rootId && t.RepositoryFullName == normalized);
        if (!await tasks.AnyAsync(t => t.Id == rootId, cancellationToken) || await tasks.Where(NotFinal).AnyAsync(cancellationToken))
        {
            return false;
        }

        await tasks.ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    public async Task<List<TodoTaskRun>> GetRunsAsync(string repositoryFullName, int max, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var roots = await db.TodoTasks.AsNoTracking()
            .Where(t => t.ParentId == null && t.RepositoryFullName == normalized)
            .OrderByDescending(t => t.SysId)
            .Take(max)
            .ToListAsync(cancellationToken);
        var rootIds = roots.Select(r => r.Id).ToList();
        var subTasks = (await db.TodoTasks.AsNoTracking()
                .Where(t => rootIds.Contains(t.RootId) && t.ParentId != null)
                .OrderBy(t => t.SysId)
                .ToListAsync(cancellationToken))
            .ToLookup(t => t.RootId);
        return roots.Select(root => new TodoTaskRun(root.ToContract(), subTasks[root.Id].Select(t => t.ToContract()).ToList())).ToList();
    }

    public async Task<TodoTaskRun?> GetRunAsync(string repositoryFullName, Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var tasks = await db.TodoTasks.AsNoTracking()
            .Where(t => t.RootId == rootId && t.RepositoryFullName == normalized)
            .OrderBy(t => t.SysId)
            .ToListAsync(cancellationToken);
        var root = tasks.SingleOrDefault(t => t.Id == rootId);
        return root is null ? null : new TodoTaskRun(root.ToContract(), tasks.Where(t => t.Id != rootId).Select(t => t.ToContract()).ToList());
    }

    public async Task<TodoTaskStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TodoTasks.AsNoTracking().Where(t => t.Id == id).Select(t => (TodoTaskStatus?)t.Status).SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Ends <paramref name="taskId"/> when it waits and all its sub-tasks have ended, then its parent in the same way. A
    /// parent fails when a sub-task failed, is cancelled when one was cancelled, and succeeds otherwise. Sub-tasks that
    /// end at the same time can both get here; the conditional update lets only one of them end the parent.
    /// </summary>
    private static async Task<int> EndAncestorsAsync(AppDbContext db, Guid? taskId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = 0;
        while (taskId is { } id)
        {
            var counts = await db.TodoTasks.AsNoTracking()
                .Where(t => t.ParentId == id)
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            if (counts.Count == 0 || counts.Any(c => !c.Status.IsFinal))
            {
                return changed;
            }

            var total = counts.Sum(c => c.Count);
            var failed = counts.Where(c => c.Status == TodoTaskStatus.Failed).Sum(c => c.Count);
            var cancelled = counts.Where(c => c.Status == TodoTaskStatus.Cancelled).Sum(c => c.Count);
            var status = failed > 0 ? TodoTaskStatus.Failed : cancelled > 0 ? TodoTaskStatus.Cancelled : TodoTaskStatus.Succeeded;
            var updated = failed > 0
                ? await db.TodoTasks
                    .Where(t => t.Id == id && t.Status == TodoTaskStatus.Waiting)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.Status, status)
                        .SetProperty(t => t.Error, $"{failed} of {total} sub-tasks failed.")
                        .SetProperty(t => t.CompletedAt, now), cancellationToken)
                : await db.TodoTasks
                    .Where(t => t.Id == id && t.Status == TodoTaskStatus.Waiting)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.Status, status)
                        .SetProperty(t => t.CompletedAt, now), cancellationToken);
            if (updated == 0)
            {
                return changed;
            }

            changed++;
            taskId = await db.TodoTasks.AsNoTracking().Where(t => t.Id == id).Select(t => t.ParentId).SingleAsync(cancellationToken);
        }
        return changed;
    }

    private static Entities.TodoTask New(NewTodoTask task, Guid id, Guid rootId, Guid? parentId, string repositoryFullName, int maxAttempts, DateTimeOffset now)
    {
        return new Entities.TodoTask
        {
            Id = id,
            RootId = rootId,
            ParentId = parentId,
            Kind = task.Kind,
            Status = TodoTaskStatus.Pending,
            RepositoryFullName = repositoryFullName,
            Title = Truncate(task.Title, TodoTask.MaxTitleLength) ?? task.Kind.ToString(),
            Payload = task.Payload,
            MaxAttempts = maxAttempts,
            CreatedAt = now
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
    }
}

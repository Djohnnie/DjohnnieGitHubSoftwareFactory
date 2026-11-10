using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class WorkItemStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IWorkItemStore
{
    public async Task<List<WorkItem>> GetAllAsync(string repositoryFullName, IReadOnlyList<WorkItemType>? types = null, Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = Query(db.WorkItems.AsNoTracking(), repositoryFullName);
        if (types is { Count: > 0 })
        {
            var selected = types.ToList();
            query = query.Where(e => selected.Contains(e.Type));
        }
        if (parentId is not null)
        {
            query = query.Where(e => e.ParentId == parentId);
        }
        var entities = await query.OrderBy(e => e.Status).ThenByDescending(e => e.UpdatedAt).ToListAsync(cancellationToken);
        return entities.Select(e => e.ToContract()).ToList();
    }

    public async Task<WorkItem?> GetAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await Query(db.WorkItems.AsNoTracking(), repositoryFullName)
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return entity?.ToContract();
    }

    public async Task<List<WorkItemCounts>> GetCountsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var counts = await db.WorkItems.AsNoTracking()
            .Where(e => e.Repository.FullName == normalized)
            .GroupBy(e => e.Type)
            .Select(g => new WorkItemCounts(
                g.Key,
                g.Count(e => e.Status == WorkItemStatus.Draft),
                g.Count(e => e.Status == WorkItemStatus.Accepted || e.Status == WorkItemStatus.InProgress),
                g.Count(e => e.Status == WorkItemStatus.Done)))
            .ToDictionaryAsync(c => c.Type, cancellationToken);
        return WorkItemTypeRules.All.Select(type => counts.GetValueOrDefault(type) ?? new WorkItemCounts(type, 0, 0, 0)).ToList();
    }

    public async Task<WorkItem> SaveAsync(WorkItem item, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var entity = item.Id == Guid.Empty
            ? new Entities.WorkItem
            {
                Repository = await db.GetOrAddRepositoryAsync(item.RepositoryFullName, cancellationToken),
                Type = item.Type,
                CreatedAt = now
            }
            : await FindAsync(db, item.RepositoryFullName, item.Id, cancellationToken);

        if (entity.Status == WorkItemStatus.Draft)
        {
            entity.Title = item.Title.Trim();
            entity.Description = item.Description ?? string.Empty;
            await SetParentAsync(db, entity, item.RepositoryFullName, item.ParentId, cancellationToken);
        }
        else if (!entity.Type.CanBeAssigned || entity.Status != WorkItemStatus.Accepted)
        {
            var state = entity.Type.CanBeAssigned ? "assigned" : "accepted";
            throw new DomainRuleException($"This {Name(entity.Type)} is already {state} and can no longer be changed.");
        }

        // Epics and features only give structure; they are never assigned to an agent.
        var assignable = entity.Type.CanBeAssigned;
        entity.CustomAgent = assignable ? NullIfEmpty(item.CustomAgent) : null;
        entity.Model = assignable ? NullIfEmpty(item.Model) : null;
        entity.BaseBranch = assignable ? NullIfEmpty(item.BaseBranch) : null;
        entity.CustomInstructions = assignable ? NullIfEmpty(item.CustomInstructions) : null;
        entity.UpdatedAt = now;
        if (item.Id == Guid.Empty)
        {
            db.WorkItems.Add(entity);
        }
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToContract();
    }

    public async Task<List<WorkItem>> AddGeneratedChildrenAsync(string repositoryFullName, Guid parentId, IReadOnlyList<GeneratedWorkItem> items, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var parent = await FindAsync(db, repositoryFullName, parentId, cancellationToken);
        if (parent.Type.GeneratedChildType is not { } childType)
        {
            throw new DomainRuleException($"A {Name(parent.Type)} can't be split into child items.");
        }

        var now = timeProvider.GetUtcNow();
        var children = items.Select(item => new Entities.WorkItem
        {
            Repository = parent.Repository,
            Parent = parent,
            Type = childType,
            Title = item.Title.Trim(),
            Description = item.Description,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
        db.WorkItems.AddRange(children);
        await db.SaveChangesAsync(cancellationToken);
        return children.Select(child => child.ToContract()).ToList();
    }

    public async Task DeleteAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await Query(db.WorkItems, repositoryFullName).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        var children = await db.WorkItems.CountAsync(e => e.ParentId == id, cancellationToken);
        if (children > 0)
        {
            var noun = children == 1 ? "child item" : "child items";
            throw new DomainRuleException($"'{entity.Title}' still has {children} {noun}. Delete them or move them to another parent first.");
        }
        db.WorkItems.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> BeginAcceptAsync(string repositoryFullName, Guid id, TimeSpan claimTimeout, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, repositoryFullName, id, cancellationToken);
        if (entity.Status != WorkItemStatus.Draft)
        {
            throw new DomainRuleException($"Only a draft {Name(entity.Type)} can be accepted.");
        }

        var now = timeProvider.GetUtcNow();
        if (entity.AcceptStartedAt is { } started)
        {
            if (now - started < claimTimeout)
            {
                throw BeingAccepted(entity.Type);
            }

            // An expired claim: that attempt died without ending, so it may have created the issue.
            entity.AcceptIssueSince = entity.AcceptIssueSince is { } since && since < started ? since : started;
        }

        var issueSince = entity.AcceptIssueSince;
        entity.AcceptStartedAt = now;
        try
        {
            // Status and AcceptStartedAt are concurrency tokens: a concurrent claim makes this save fail.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Djohnnie.SoftwareFactory.Contracts.PersistenceException ex) when (ex.InnerException is DbUpdateConcurrencyException)
        {
            throw BeingAccepted(entity.Type);
        }
        return issueSince;
    }

    public async Task EndFailedAcceptAsync(string repositoryFullName, Guid id, bool issueMayExist, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, repositoryFullName, id, cancellationToken);
        if (entity.Status != WorkItemStatus.Draft || entity.AcceptStartedAt is not { } started)
        {
            return;
        }

        // Keep the earliest attempt that may have created the issue until an attempt finishes.
        if (issueMayExist && (entity.AcceptIssueSince is null || started < entity.AcceptIssueSince))
        {
            entity.AcceptIssueSince = started;
        }
        entity.AcceptStartedAt = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkItem> MarkAcceptedAsync(string repositoryFullName, Guid id, long issueId, int issueNumber, string issueUrl, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, repositoryFullName, id, cancellationToken);
        if (entity.Status != WorkItemStatus.Draft)
        {
            throw new DomainRuleException($"This {Name(entity.Type)} was already accepted.");
        }
        entity.Status = WorkItemStatus.Accepted;
        entity.AcceptStartedAt = null;
        entity.AcceptIssueSince = null;
        entity.IssueId = issueId;
        entity.IssueNumber = issueNumber;
        entity.IssueUrl = issueUrl;
        entity.AcceptedAt = entity.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToContract();
    }

    public async Task<WorkItem> MarkAssignedAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, repositoryFullName, id, cancellationToken);
        entity.Status = WorkItemStatus.InProgress;
        entity.AssignedAt = entity.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToContract();
    }

    public async Task<WorkItem> ApplyIssueAsync(string repositoryFullName, Guid id, WorkItemIssueSnapshot issue, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, repositoryFullName, id, cancellationToken);
        if (entity.IssueNumber is null)
        {
            throw new DomainRuleException($"This {Name(entity.Type)} has no GitHub issue yet.");
        }

        var now = timeProvider.GetUtcNow();
        entity.Title = issue.Title;
        entity.Description = issue.Description;
        entity.IssueId = issue.IssueId;
        entity.IssueUrl = issue.IssueUrl;
        entity.Status = issue.IsClosed
            ? WorkItemStatus.Done
            : entity.Type.CanBeAssigned && issue.IsAssignedToCopilot ? WorkItemStatus.InProgress : WorkItemStatus.Accepted;
        entity.AssignedAt = entity.Status switch
        {
            WorkItemStatus.Accepted => null,
            WorkItemStatus.InProgress => entity.AssignedAt ?? now,
            _ => entity.AssignedAt
        };

        // Without a progress snapshot (GitHub didn't show the pull requests), the stored progress stays.
        if (!entity.Type.CanBeAssigned || issue.Progress is not null)
        {
            var progress = entity.Type.CanBeAssigned ? issue.Progress! : WorkItemProgressSnapshot.None;
            entity.Progress = progress.Progress;
            entity.PullRequestNumber = progress.PullRequestNumber;
            entity.PullRequestUrl = progress.PullRequestUrl;
            entity.PullRequestChecks = progress.Checks;
            entity.AgentRunUrl = progress.AgentRunUrl;
        }

        if (db.ChangeTracker.HasChanges())
        {
            entity.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
        return entity.ToContract();
    }

    public async Task SetIssueIdAsync(string repositoryFullName, Guid id, long issueId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, repositoryFullName, id, cancellationToken);
        entity.IssueId = issueId;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SetParentAsync(AppDbContext db, Entities.WorkItem entity, string repositoryFullName, Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            if (entity.Type.RequiresParent)
            {
                throw new DomainRuleException($"A {Name(entity.Type)} must be part of {ParentNames(entity.Type)}.");
            }
            entity.Parent = null;
            entity.ParentId = null;
            return;
        }

        var parent = await Query(db.WorkItems, repositoryFullName).SingleOrDefaultAsync(e => e.Id == parentId, cancellationToken)
            ?? throw new DomainRuleException("The chosen parent no longer exists in this repository.");
        if (!entity.Type.CanHaveParent(parent.Type))
        {
            throw new DomainRuleException($"A {Name(entity.Type)} can only be part of {ParentNames(entity.Type)}, not of {WithArticle(parent.Type)}.");
        }
        entity.Parent = parent;
        entity.ParentId = parent.Id;
    }

    /// <summary>The items of one repository, with the repository and parent loaded (needed to map to the contract).</summary>
    private static IQueryable<Entities.WorkItem> Query(IQueryable<Entities.WorkItem> items, string repositoryFullName)
    {
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        return items.Include(e => e.Repository).Include(e => e.Parent).Where(e => e.Repository.FullName == normalized);
    }

    private static async Task<Entities.WorkItem> FindAsync(AppDbContext db, string repositoryFullName, Guid id, CancellationToken cancellationToken)
    {
        return await Query(db.WorkItems, repositoryFullName).SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
    }

    private static DomainRuleException BeingAccepted(WorkItemType type)
    {
        return new DomainRuleException($"This {Name(type)} is being accepted right now. Reload in a moment.");
    }

    private static string Name(WorkItemType type)
    {
        return type.DisplayName.ToLowerInvariant();
    }

    private static string WithArticle(WorkItemType type)
    {
        return (type == WorkItemType.Epic ? "an " : "a ") + Name(type);
    }

    /// <summary>E.g. "an epic", or "an epic, a feature or a user story".</summary>
    private static string ParentNames(WorkItemType type)
    {
        var names = type.ParentTypes.Select(WithArticle).ToList();
        return names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names[..^1])} or {names[^1]}";
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

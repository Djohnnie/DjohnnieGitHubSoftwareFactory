using Djohnnie.SoftwareFactory.Contracts.Epics;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class EpicStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IEpicStore
{
    public async Task<List<Epic>> GetAllAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entities = await Query(db.Epics.AsNoTracking(), repositoryFullName)
            .OrderBy(e => e.Status).ThenByDescending(e => e.UpdatedAt)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToContract()).ToList();
    }

    public async Task<Epic?> GetAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await Query(db.Epics.AsNoTracking(), repositoryFullName)
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return entity?.ToContract();
    }

    public async Task<Epic> SaveAsync(Epic epic, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var entity = epic.Id == 0
            ? new Entities.Epic
            {
                Repository = await db.GetOrAddRepositoryAsync(epic.RepositoryFullName, cancellationToken),
                CreatedAt = now
            }
            : await FindAsync(db, epic.RepositoryFullName, epic.Id, cancellationToken);

        if (entity.Status == WorkItemStatus.Draft)
        {
            entity.Title = epic.Title.Trim();
            entity.Description = epic.Description ?? string.Empty;
        }
        else if (entity.Status != WorkItemStatus.Accepted)
        {
            throw new InvalidOperationException("This epic is already assigned and can no longer be changed.");
        }

        entity.CustomAgent = NullIfEmpty(epic.CustomAgent);
        entity.Model = NullIfEmpty(epic.Model);
        entity.BaseBranch = NullIfEmpty(epic.BaseBranch);
        entity.CustomInstructions = NullIfEmpty(epic.CustomInstructions);
        entity.UpdatedAt = now;
        if (epic.Id == 0)
        {
            db.Epics.Add(entity);
        }
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToContract();
    }

    public async Task DeleteAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await Query(db.Epics, repositoryFullName).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is not null)
        {
            db.Epics.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<Epic> MarkAcceptedAsync(string repositoryFullName, int id, int issueNumber, string issueUrl, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, repositoryFullName, id, cancellationToken);
        entity.Status = WorkItemStatus.Accepted;
        entity.IssueNumber = issueNumber;
        entity.IssueUrl = issueUrl;
        entity.AcceptedAt = entity.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToContract();
    }

    public async Task<Epic> MarkAssignedAsync(string repositoryFullName, int id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, repositoryFullName, id, cancellationToken);
        entity.Status = WorkItemStatus.InProgress;
        entity.AssignedAt = entity.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToContract();
    }

    /// <summary>The epics of one repository, with the repository loaded (needed to map to the contract).</summary>
    private static IQueryable<Entities.Epic> Query(IQueryable<Entities.Epic> epics, string repositoryFullName)
    {
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        return epics.Include(e => e.Repository).Where(e => e.Repository.FullName == normalized);
    }

    private static async Task<Entities.Epic> FindAsync(AppDbContext db, string repositoryFullName, int id, CancellationToken cancellationToken)
    {
        return await Query(db.Epics, repositoryFullName).SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("The epic no longer exists.");
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

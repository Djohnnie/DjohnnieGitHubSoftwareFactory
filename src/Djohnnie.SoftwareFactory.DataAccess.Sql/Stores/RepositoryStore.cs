using System.Linq.Expressions;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class RepositoryStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IRepositoryStore
{
    public const int RecentCount = 5;

    private static readonly Expression<Func<Repository, RepositorySummary>> ToSummary = repository => new RepositorySummary(
        repository.Id, repository.FullName, repository.Name, repository.LastAccessedAt, repository.IsFavorite, repository.IsArchived, repository.ArchivedAt);

    public async Task<List<RepositorySummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking()
            .OrderBy(repository => repository.FullName)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    public async Task<RepositorySummary?> FindAsync(string fullName, CancellationToken cancellationToken = default)
    {
        var normalized = Repository.Normalize(fullName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking()
            .Where(repository => repository.FullName == normalized)
            .Select(ToSummary)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The most recently accessed repositories that are not favorites (those are listed separately) and still exist on GitHub.
    /// </summary>
    public async Task<List<RepositorySummary>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking()
            .Where(repository => repository.LastAccessedAt != null && !repository.IsFavorite && !repository.IsArchived)
            .OrderByDescending(repository => repository.LastAccessedAt)
            .ThenBy(repository => repository.FullName)
            .Take(RecentCount)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RepositorySummary>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking()
            .Where(repository => repository.IsFavorite && !repository.IsArchived)
            .OrderBy(repository => repository.FullName)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RepositorySummary>> GetArchivedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking()
            .Where(repository => repository.IsArchived)
            .OrderBy(repository => repository.FullName)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    public async Task RecordAccessAsync(string fullName, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await db.GetOrAddRepositoryAsync(fullName, cancellationToken);
        repository.LastAccessedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetFavoriteAsync(string fullName, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await db.GetOrAddRepositoryAsync(fullName, cancellationToken);
        if (isFavorite && repository.IsArchived)
        {
            throw new DomainRuleException($"{repository.FullName} was removed from GitHub, so it can't be a favorite.");
        }
        repository.IsFavorite = isFavorite;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SetArchivedAsync(string fullName, bool isArchived, CancellationToken cancellationToken = default)
    {
        var normalized = Repository.Normalize(fullName);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await db.Repositories.SingleOrDefaultAsync(r => r.FullName == normalized, cancellationToken);
        if (repository is null || repository.IsArchived == isArchived)
        {
            return false;
        }

        repository.IsArchived = isArchived;
        repository.ArchivedAt = isArchived ? timeProvider.GetUtcNow() : null;
        if (isArchived)
        {
            // Out of the favorites for good: a repository that comes back on GitHub doesn't become a favorite again by itself.
            repository.IsFavorite = false;
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

public sealed class RepositoryStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider)
{
    public const int RecentCount = 5;

    /// <summary>The most recently accessed repositories that are not favorites (those are listed separately).</summary>
    public async Task<List<RepositorySummary>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking()
            .Where(repository => repository.LastAccessedAt != null && !repository.IsFavorite)
            .OrderByDescending(repository => repository.LastAccessedAt)
            .ThenBy(repository => repository.FullName)
            .Take(RecentCount)
            .Select(repository => new RepositorySummary(repository.Id, repository.FullName, repository.Name, repository.LastAccessedAt, repository.IsFavorite))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RepositorySummary>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking()
            .Where(repository => repository.IsFavorite)
            .OrderBy(repository => repository.FullName)
            .Select(repository => new RepositorySummary(repository.Id, repository.FullName, repository.Name, repository.LastAccessedAt, repository.IsFavorite))
            .ToListAsync(cancellationToken);
    }

    public async Task RecordAccessAsync(string fullName, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await GetOrAddAsync(db, fullName, cancellationToken);
        repository.LastAccessedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetFavoriteAsync(string fullName, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await GetOrAddAsync(db, fullName, cancellationToken);
        repository.IsFavorite = isFavorite;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Finds the repository row, or adds (but does not save) a new one.</summary>
    internal static async Task<Repository> GetOrAddAsync(AppDbContext db, string fullName, CancellationToken cancellationToken)
    {
        var normalized = Repository.Normalize(fullName);
        var repository = await db.Repositories.SingleOrDefaultAsync(r => r.FullName == normalized, cancellationToken);
        if (repository is null)
        {
            repository = Repository.Create(normalized);
            db.Repositories.Add(repository);
        }
        return repository;
    }
}

using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class RepositoryStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IRepositoryStore
{
    public const int RecentCount = 5;

    public async Task<List<RepositorySummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking()
            .OrderBy(repository => repository.FullName)
            .Select(repository => new RepositorySummary(repository.Id, repository.FullName, repository.Name, repository.LastAccessedAt, repository.IsFavorite))
            .ToListAsync(cancellationToken);
    }

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
        var repository = await db.GetOrAddRepositoryAsync(fullName, cancellationToken);
        repository.LastAccessedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetFavoriteAsync(string fullName, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await db.GetOrAddRepositoryAsync(fullName, cancellationToken);
        repository.IsFavorite = isFavorite;
        await db.SaveChangesAsync(cancellationToken);
    }
}

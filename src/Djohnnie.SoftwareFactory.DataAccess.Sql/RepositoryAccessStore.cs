using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

public sealed class RepositoryAccessStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider)
{
    public async Task<List<RecentRepository>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.RepositoryAccesses.AsNoTracking()
            .OrderByDescending(repository => repository.LastAccessedAt)
            .ThenBy(repository => repository.FullName)
            .Take(5)
            .Select(repository => new RecentRepository(repository.FullName, repository.LastAccessedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task RecordAccessAsync(string fullName, CancellationToken cancellationToken = default)
    {
        // GitHub repository names are case-insensitive; use one stable key across visits.
        fullName = fullName.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await db.RepositoryAccesses.FindAsync([fullName], cancellationToken);
        if (repository is null)
        {
            repository = new RepositoryAccess { FullName = fullName };
            db.RepositoryAccesses.Add(repository);
        }
        repository.LastAccessedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }
}

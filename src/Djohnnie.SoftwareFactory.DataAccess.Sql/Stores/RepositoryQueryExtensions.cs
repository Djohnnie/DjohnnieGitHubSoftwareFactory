using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

internal static class RepositoryQueryExtensions
{
    extension(AppDbContext db)
    {
        /// <summary>
        /// Finds the repository row, or inserts (and saves) a new one. When a concurrent request inserted the same
        /// repository first, the unique index refuses this insert and the row of the other request is returned.
        /// Call it before making other changes to <paramref name="db"/>, because it saves the context.
        /// </summary>
        public async Task<Repository> GetOrAddRepositoryAsync(string fullName, CancellationToken cancellationToken)
        {
            var normalized = Repository.Normalize(fullName);
            if (await db.Repositories.SingleOrDefaultAsync(r => r.FullName == normalized, cancellationToken) is { } existing)
            {
                return existing;
            }

            var repository = Repository.Create(normalized);
            db.Repositories.Add(repository);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return repository;
            }
            catch (PersistenceException ex) when (ex.InnerException is DbUpdateException)
            {
                db.Entry(repository).State = EntityState.Detached;
                var inserted = await db.Repositories.SingleOrDefaultAsync(r => r.FullName == normalized, cancellationToken);
                if (inserted is null)
                {
                    // Not a concurrent insert: something else went wrong.
                    throw;
                }
                return inserted;
            }
        }
    }
}

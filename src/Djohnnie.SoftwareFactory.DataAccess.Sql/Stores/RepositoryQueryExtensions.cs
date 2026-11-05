using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

internal static class RepositoryQueryExtensions
{
    extension(AppDbContext db)
    {
        /// <summary>Finds the repository row, or adds (but does not save) a new one.</summary>
        public async Task<Repository> GetOrAddRepositoryAsync(string fullName, CancellationToken cancellationToken)
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
}

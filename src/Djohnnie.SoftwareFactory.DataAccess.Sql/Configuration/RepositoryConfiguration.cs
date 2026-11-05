using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class RepositoryConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureRepository()
        {
            return modelBuilder.Entity<Repository>(repository =>
            {
                repository.HasIndex(r => r.FullName).IsUnique();
                repository.HasIndex(r => r.LastAccessedAt);
            });
        }
    }
}

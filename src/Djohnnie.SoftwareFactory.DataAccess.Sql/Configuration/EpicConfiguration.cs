using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class EpicConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        /// <remarks>Everything that belongs to a repository blocks deleting it (no cascades).</remarks>
        public ModelBuilder ConfigureEpic()
        {
            return modelBuilder.Entity<Epic>(epic =>
            {
                epic.HasOne(e => e.Repository).WithMany(r => r.Epics)
                    .HasForeignKey(e => e.RepositoryId).OnDelete(DeleteBehavior.Restrict);
                epic.HasIndex(e => new { e.RepositoryId, e.Status });
            });
        }
    }
}

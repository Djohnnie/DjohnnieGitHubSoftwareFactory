using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class MindmapConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        /// <remarks>Everything that belongs to a repository blocks deleting it (no cascades).</remarks>
        public ModelBuilder ConfigureMindmap()
        {
            return modelBuilder.Entity<Mindmap>(mindmap =>
            {
                mindmap.HasOne(m => m.Repository).WithMany()
                    .HasForeignKey(m => m.RepositoryId).OnDelete(DeleteBehavior.Restrict);
                mindmap.HasIndex(m => m.RepositoryId);
            });
        }
    }
}

using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class KnowledgeBaseConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        /// <remarks>A folder that still contains folders or files can't be deleted (no cascades).</remarks>
        public ModelBuilder ConfigureKnowledgeBase()
        {
            modelBuilder.Entity<KnowledgeFolder>(folder =>
            {
                folder.HasOne(f => f.Repository).WithMany()
                    .HasForeignKey(f => f.RepositoryId).OnDelete(DeleteBehavior.Restrict);
                folder.HasOne(f => f.Parent).WithMany()
                    .HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.Restrict);
                // Without a filter, SQL Server treats NULL parents as equal, so names are unique at the top of a repository's library too.
                folder.HasIndex(f => new { f.RepositoryId, f.ParentId, f.Name }).IsUnique().HasFilter(null);
            });

            return modelBuilder.Entity<KnowledgeFile>(file =>
            {
                file.HasOne(f => f.Folder).WithMany()
                    .HasForeignKey(f => f.FolderId).OnDelete(DeleteBehavior.Restrict);
                file.HasIndex(f => f.FolderId);
                // Like everything else that belongs to a repository, a file blocks deleting it (no cascades).
                file.HasOne(f => f.Repository).WithMany()
                    .HasForeignKey(f => f.RepositoryId).OnDelete(DeleteBehavior.Restrict);
                file.HasIndex(f => f.RepositoryId);
            });
        }
    }
}

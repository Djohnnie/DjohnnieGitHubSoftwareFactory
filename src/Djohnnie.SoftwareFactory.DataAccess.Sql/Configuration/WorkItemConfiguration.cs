using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class WorkItemConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        /// <remarks>
        /// Everything that belongs to a repository blocks deleting it (no cascades). A parent can't be deleted
        /// while it still has children; the store explains that before the database would refuse it.
        /// </remarks>
        public ModelBuilder ConfigureWorkItem()
        {
            return modelBuilder.Entity<WorkItem>(item =>
            {
                item.HasOne(e => e.Repository).WithMany(r => r.WorkItems)
                    .HasForeignKey(e => e.RepositoryId).OnDelete(DeleteBehavior.Restrict);
                item.HasOne(e => e.Parent).WithMany(e => e.Children)
                    .HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
                item.HasIndex(e => new { e.RepositoryId, e.Type, e.Status });
            });
        }

        public ModelBuilder ConfigureWorkItemTemplate()
        {
            return modelBuilder.Entity<WorkItemTemplate>(template =>
            {
                template.HasIndex(t => t.Type);
            });
        }
    }
}

using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class TodoTaskConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureTodoTask()
        {
            modelBuilder.Entity<TodoTask>(entry =>
            {
                // Stored by name to keep rows readable; the dispatcher looks tasks up by status.
                entry.Property(e => e.Kind).HasConversion<string>().HasMaxLength(50);
                entry.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
                entry.HasIndex(e => new { e.Status, e.NotBefore });
                entry.HasIndex(e => e.RootId);
                entry.HasIndex(e => e.ParentId);
                entry.HasIndex(e => new { e.RepositoryFullName, e.CreatedAt });
            });
            return modelBuilder;
        }
    }
}

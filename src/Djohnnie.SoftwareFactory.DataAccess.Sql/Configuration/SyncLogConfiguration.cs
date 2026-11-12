using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class SyncLogConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureSyncLog()
        {
            modelBuilder.Entity<SyncLogEvent>(entry =>
            {
                // The level stays a number, so "warnings and up" is a range; the kind is stored by name to keep rows readable.
                entry.Property(e => e.Kind).HasConversion<string>().HasMaxLength(50);
                entry.HasIndex(e => e.LastOccurredAt);
                entry.HasIndex(e => e.Level);
                entry.HasIndex(e => e.RepositoryFullName);
            });
            return modelBuilder;
        }
    }
}

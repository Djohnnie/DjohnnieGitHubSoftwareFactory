using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class InternalAgentConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureInternalAgent()
        {
            return modelBuilder.Entity<InternalAgent>(agent =>
            {
                agent.HasIndex(a => a.Key).IsUnique();
                agent.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
            });
        }
    }
}

using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class AgentConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        /// <remarks>Everything that belongs to a repository blocks deleting it (no cascades).</remarks>
        public ModelBuilder ConfigureAgent()
        {
            return modelBuilder.Entity<Agent>(agent =>
            {
                agent.HasOne(a => a.Repository).WithMany(r => r.Agents)
                    .HasForeignKey(a => a.RepositoryId).OnDelete(DeleteBehavior.Restrict);
                agent.HasIndex(a => new { a.RepositoryId, a.FileName }).IsUnique();
            });
        }
    }
}

using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class AgentRunConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureAgentRun()
        {
            modelBuilder.Entity<AgentRun>(entry =>
            {
                // Stored by name to keep rows readable; the dispatcher looks runs up by status.
                entry.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
                entry.Property(e => e.PremiumRequests).HasPrecision(18, 4);
                entry.HasIndex(e => e.Status);
                entry.HasIndex(e => e.WorkItemId);
            });
            modelBuilder.Entity<AgentRunModelUsage>(usage =>
            {
                usage.HasOne<AgentRun>().WithMany().HasForeignKey(u => u.AgentRunId).OnDelete(DeleteBehavior.Cascade);
                // Deleting a catalog model keeps what its sessions cost, without the link.
                usage.HasOne<AiModel>().WithMany().HasForeignKey(u => u.AiModelId).OnDelete(DeleteBehavior.SetNull);
                usage.Property(u => u.PremiumRequests).HasPrecision(18, 4);
            });
            return modelBuilder;
        }
    }
}

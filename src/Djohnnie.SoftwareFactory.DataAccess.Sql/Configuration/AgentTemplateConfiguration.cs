using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class AgentTemplateConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureAgentTemplate()
        {
            return modelBuilder.Entity<AgentTemplate>(template =>
            {
                template.HasIndex(t => t.FileName).IsUnique();
            });
        }
    }
}

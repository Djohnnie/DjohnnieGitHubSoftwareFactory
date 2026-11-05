using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class AiModelConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureAiModel()
        {
            return modelBuilder.Entity<AiModel>(model =>
            {
                model.HasIndex(m => new { m.Name, m.Tier }).IsUnique();
                model.HasIndex(m => m.ModelId);
                model.Property(m => m.InputPrice).HasPrecision(18, 4);
                model.Property(m => m.CachedInputPrice).HasPrecision(18, 4);
                model.Property(m => m.CacheWritePrice).HasPrecision(18, 4);
                model.Property(m => m.OutputPrice).HasPrecision(18, 4);
            });
        }
    }
}

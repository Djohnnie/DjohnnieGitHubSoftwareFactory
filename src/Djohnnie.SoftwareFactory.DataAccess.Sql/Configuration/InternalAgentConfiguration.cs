using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class InternalAgentConfiguration
{
    private const int PricePrecision = 18;
    private const int PriceScale = 6;
    private const int CostPrecision = 18;

    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureInternalAgent()
        {
            modelBuilder.Entity<InternalModel>(model =>
            {
                model.HasIndex(m => m.Name).IsUnique();
                model.Property(m => m.InputPricePerMillion).HasPrecision(PricePrecision, PriceScale);
                model.Property(m => m.OutputPricePerMillion).HasPrecision(PricePrecision, PriceScale);
                model.Property(m => m.CachedInputPricePerMillion).HasPrecision(PricePrecision, PriceScale);
            });

            modelBuilder.Entity<InternalAgent>(agent =>
            {
                agent.HasIndex(a => a.Key).IsUnique();
                agent.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
                agent.HasOne(a => a.InternalModel).WithMany().HasForeignKey(a => a.InternalModelId).OnDelete(DeleteBehavior.Restrict);
                agent.Property(a => a.RequestCount).IsConcurrencyToken();
                agent.Property(a => a.TotalInputCost).HasPrecision(CostPrecision, Contracts.InternalModels.InternalModel.CostDecimals);
                agent.Property(a => a.TotalOutputCost).HasPrecision(CostPrecision, Contracts.InternalModels.InternalModel.CostDecimals);
            });

            return modelBuilder.Entity<InternalAgentUsage>(usage =>
            {
                usage.HasIndex(u => u.InternalAgentId);
                usage.Property(u => u.Source).HasConversion<string>().HasMaxLength(50);
                usage.HasOne(u => u.InternalAgent).WithMany().HasForeignKey(u => u.InternalAgentId).OnDelete(DeleteBehavior.Restrict);
                usage.Property(u => u.InputPricePerMillion).HasPrecision(PricePrecision, PriceScale);
                usage.Property(u => u.OutputPricePerMillion).HasPrecision(PricePrecision, PriceScale);
                usage.Property(u => u.CachedInputPricePerMillion).HasPrecision(PricePrecision, PriceScale);
                usage.Property(u => u.InputCost).HasPrecision(CostPrecision, Contracts.InternalModels.InternalModel.CostDecimals);
                usage.Property(u => u.OutputCost).HasPrecision(CostPrecision, Contracts.InternalModels.InternalModel.CostDecimals);
            });
        }
    }
}

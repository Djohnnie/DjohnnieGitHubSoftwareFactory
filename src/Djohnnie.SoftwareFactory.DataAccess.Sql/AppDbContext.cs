using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AiModel> AiModels => Set<AiModel>();
    public DbSet<Epic> Epics => Set<Epic>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<EpicTemplate> EpicTemplates => Set<EpicTemplate>();
    public DbSet<RepositoryAccess> RepositoryAccesses => Set<RepositoryAccess>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new Djohnnie.SoftwareFactory.Contracts.PersistenceException("Could not save changes to the database.", ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RepositoryAccess>()
            .HasIndex(repository => repository.LastAccessedAt);

        modelBuilder.Entity<AgentTemplate>(template =>
        {
            template.HasIndex(t => t.FileName).IsUnique();
        });

        modelBuilder.Entity<Epic>(epic =>
        {
            epic.HasIndex(e => new { e.RepositoryFullName, e.Status });
        });

        modelBuilder.Entity<AiModel>(model =>
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

using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AiModel> AiModels => Set<AiModel>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<Epic> Epics => Set<Epic>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<EpicTemplate> EpicTemplates => Set<EpicTemplate>();

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
        modelBuilder.Entity<Repository>(repository =>
        {
            repository.HasIndex(r => r.FullName).IsUnique();
            repository.HasIndex(r => r.LastAccessedAt);
        });

        // Everything that belongs to a repository blocks deleting it (no cascades).
        modelBuilder.Entity<Epic>(epic =>
        {
            epic.HasOne(e => e.Repository).WithMany(r => r.Epics)
                .HasForeignKey(e => e.RepositoryId).OnDelete(DeleteBehavior.Restrict);
            epic.HasIndex(e => new { e.RepositoryId, e.Status });
        });

        modelBuilder.Entity<Agent>(agent =>
        {
            agent.HasOne(a => a.Repository).WithMany(r => r.Agents)
                .HasForeignKey(a => a.RepositoryId).OnDelete(DeleteBehavior.Restrict);
            agent.HasIndex(a => new { a.RepositoryId, a.FileName }).IsUnique();
        });

        modelBuilder.Entity<AgentTemplate>(template =>
        {
            template.HasIndex(t => t.FileName).IsUnique();
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

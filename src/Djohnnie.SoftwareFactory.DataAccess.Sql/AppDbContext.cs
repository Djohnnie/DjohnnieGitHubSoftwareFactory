using Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;
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
        modelBuilder
            .ConfigureRepository()
            .ConfigureEpic()
            .ConfigureAgent()
            .ConfigureAgentTemplate()
            .ConfigureAiModel();
    }
}

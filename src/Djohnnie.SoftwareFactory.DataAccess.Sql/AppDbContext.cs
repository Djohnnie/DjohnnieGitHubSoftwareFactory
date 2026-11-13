using Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AiModel> AiModels => Set<AiModel>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<WorkItemTemplate> WorkItemTemplates => Set<WorkItemTemplate>();
    public DbSet<InternalAgent> InternalAgents => Set<InternalAgent>();
    public DbSet<InternalModel> InternalModels => Set<InternalModel>();
    public DbSet<InternalAgentUsage> InternalAgentUsages => Set<InternalAgentUsage>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<SyncLogEvent> SyncLogEvents => Set<SyncLogEvent>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new Djohnnie.SoftwareFactory.Contracts.PersistenceException("The data was changed in the meantime. Reload and try again.", ex);
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
            .ConfigureWorkItem()
            .ConfigureWorkItemTemplate()
            .ConfigureAgent()
            .ConfigureAgentTemplate()
            .ConfigureSkill()
            .ConfigureAiModel()
            .ConfigureInternalAgent()
            .ConfigureAuthSession()
            .ConfigureChatConversation()
            .ConfigureSyncLog()
            .ConfigureEntityKeys();
    }
}

using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class AgentTemplateStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IAgentTemplateStore
{
    public async Task<List<AgentTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var templates = await db.AgentTemplates.AsNoTracking().ToListAsync(cancellationToken);
        return templates.Select(t => t.ToContract()).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<AgentTemplate?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var template = await db.AgentTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template?.ToContract();
    }

    public async Task<AgentTemplate> SaveAsync(AgentTemplate value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.AgentTemplates.AnyAsync(t => t.Id != value.Id && t.FileName == value.FileName, cancellationToken))
        {
            throw new DomainRuleException($"Another template already uses the file name '{value.FileName}'.");
        }

        var now = timeProvider.GetUtcNow();
        var template = value.Id == 0
            ? new Entities.AgentTemplate { CreatedAt = now }
            : await db.AgentTemplates.SingleOrDefaultAsync(t => t.Id == value.Id, cancellationToken)
                ?? throw new DomainRuleException("The template no longer exists.");
        template.FileName = value.FileName;
        template.Name = value.Name;
        template.Description = value.Description;
        template.Content = value.Content;
        template.UpdatedAt = now;
        if (value.Id == 0)
        {
            db.AgentTemplates.Add(template);
        }
        await db.SaveChangesAsync(cancellationToken);
        return template.ToContract();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var template = await db.AgentTemplates.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is not null)
        {
            db.AgentTemplates.Remove(template);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

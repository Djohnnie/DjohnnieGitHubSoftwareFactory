using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class WorkItemTemplateStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IWorkItemTemplateStore
{
    public async Task<List<WorkItemTemplate>> GetAllAsync(WorkItemType type, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var templates = await db.WorkItemTemplates.AsNoTracking().Where(t => t.Type == type).ToListAsync(cancellationToken);
        return templates.Select(t => t.ToContract())
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id).ToList();
    }

    public async Task<WorkItemTemplate?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var template = await db.WorkItemTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template?.ToContract();
    }

    public async Task<WorkItemTemplate> SaveAsync(WorkItemTemplate value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var template = value.Id == 0
            ? new Entities.WorkItemTemplate { Type = value.Type, CreatedAt = now }
            : await db.WorkItemTemplates.SingleOrDefaultAsync(t => t.Id == value.Id, cancellationToken)
                ?? throw new DomainRuleException("The template no longer exists.");
        template.Name = value.Name.Trim();
        template.TitleTemplate = value.TitleTemplate.Trim();
        template.Description = value.Description;
        template.UpdatedAt = now;
        if (value.Id == 0)
        {
            db.WorkItemTemplates.Add(template);
        }
        await db.SaveChangesAsync(cancellationToken);
        return template.ToContract();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var template = await db.WorkItemTemplates.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is not null)
        {
            db.WorkItemTemplates.Remove(template);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

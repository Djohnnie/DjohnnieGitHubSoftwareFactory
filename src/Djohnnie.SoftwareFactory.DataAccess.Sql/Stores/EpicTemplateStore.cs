using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class EpicTemplateStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IEpicTemplateStore
{
    public async Task<List<EpicTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var templates = await db.EpicTemplates.AsNoTracking().ToListAsync(cancellationToken);
        return templates.Select(t => t.ToContract())
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id).ToList();
    }

    public async Task<EpicTemplate?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var template = await db.EpicTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template?.ToContract();
    }

    public async Task<EpicTemplate> SaveAsync(EpicTemplate value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var template = value.Id == 0
            ? new Entities.EpicTemplate { CreatedAt = now }
            : await db.EpicTemplates.SingleOrDefaultAsync(t => t.Id == value.Id, cancellationToken)
                ?? throw new InvalidOperationException("The epic template no longer exists.");
        template.Name = value.Name.Trim();
        template.TitleTemplate = value.TitleTemplate.Trim();
        template.Description = value.Description;
        template.UpdatedAt = now;
        if (value.Id == 0)
        {
            db.EpicTemplates.Add(template);
        }
        await db.SaveChangesAsync(cancellationToken);
        return template.ToContract();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var template = await db.EpicTemplates.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is not null)
        {
            db.EpicTemplates.Remove(template);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

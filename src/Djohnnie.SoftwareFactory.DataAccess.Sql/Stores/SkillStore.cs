using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class SkillStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : ISkillStore
{
    public async Task<List<Skill>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var skills = await db.Skills.AsNoTracking().ToListAsync(cancellationToken);
        return skills.Select(s => s.ToContract()).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
    }

    public async Task<Skill?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var skill = await db.Skills.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return skill?.ToContract();
    }

    public async Task<Skill> SaveAsync(Skill value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Skills.AnyAsync(s => s.Id != value.Id && s.Name == value.Name, cancellationToken))
        {
            throw new DomainRuleException($"Another skill is already named '{value.Name}'.");
        }

        var now = timeProvider.GetUtcNow();
        var skill = value.Id == Guid.Empty
            ? new Entities.Skill { CreatedAt = now }
            : await db.Skills.SingleOrDefaultAsync(s => s.Id == value.Id, cancellationToken)
                ?? throw new DomainRuleException("The skill no longer exists.");
        skill.Name = value.Name;
        skill.Description = value.Description;
        skill.License = value.License;
        skill.Compatibility = value.Compatibility;
        skill.AllowedTools = value.AllowedTools;
        skill.Instructions = value.Instructions;
        skill.UpdatedAt = now;
        if (value.Id == Guid.Empty)
        {
            db.Skills.Add(skill);
        }

        await db.SaveChangesAsync(cancellationToken);
        return skill.ToContract();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var skill = await db.Skills.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (skill is not null)
        {
            db.Skills.Remove(skill);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

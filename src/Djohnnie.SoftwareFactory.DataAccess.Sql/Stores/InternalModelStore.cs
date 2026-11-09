using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalModels;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class InternalModelStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IInternalModelStore
{
    public async Task<List<InternalModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var models = await db.InternalModels.AsNoTracking().OrderBy(m => m.Label).ToListAsync(cancellationToken);
        return models.Select(m => m.ToContract()).ToList();
    }

    public async Task<InternalModel> SaveAsync(InternalModel value, CancellationToken cancellationToken = default)
    {
        var name = value.Name.Trim();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.InternalModels.AnyAsync(m => m.Id != value.Id && m.Name == name, cancellationToken))
        {
            throw new DomainRuleException($"A model '{name}' already exists.");
        }

        var now = timeProvider.GetUtcNow();
        Entities.InternalModel model;
        if (value.Id == Guid.Empty)
        {
            model = new Entities.InternalModel { CreatedAt = now };
            db.InternalModels.Add(model);
        }
        else
        {
            model = await db.InternalModels.SingleOrDefaultAsync(m => m.Id == value.Id, cancellationToken)
                ?? throw new DomainRuleException($"The model '{name}' no longer exists.");
        }

        model.Name = name;
        model.Label = value.Label.Trim();
        model.Description = value.Description.Trim();
        model.InputPricePerMillion = value.InputPricePerMillion;
        model.OutputPricePerMillion = value.OutputPricePerMillion;
        model.CachedInputPricePerMillion = value.CachedInputPricePerMillion;
        model.IsFlagship = value.IsFlagship;
        model.IsDefault = value.IsDefault;
        model.UpdatedAt = now;
        if (value.IsDefault)
        {
            // In the same save, so the switch to another default is atomic.
            var previous = await db.InternalModels.Where(m => m.IsDefault && m.Id != value.Id).ToListAsync(cancellationToken);
            previous.ForEach(m => m.IsDefault = false);
        }

        await db.SaveChangesAsync(cancellationToken);
        return model.ToContract();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.InternalModels.FindAsync([id], cancellationToken);
        if (model is null)
        {
            return;
        }

        var agents = await db.InternalAgents.Where(a => a.InternalModelId == id).OrderBy(a => a.Name).Select(a => a.Name).ToListAsync(cancellationToken);
        if (agents.Count > 0)
        {
            throw new DomainRuleException($"'{model.Name}' is still used by {string.Join(", ", agents)}. Choose another model for them first.");
        }

        db.InternalModels.Remove(model);
        await db.SaveChangesAsync(cancellationToken);
    }
}

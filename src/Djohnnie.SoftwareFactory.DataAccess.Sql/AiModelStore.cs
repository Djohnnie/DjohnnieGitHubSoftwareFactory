using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

public class AiModelStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider)
{
    public async Task<List<AiModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var models = await db.AiModels.AsNoTracking().ToListAsync(cancellationToken);
        return Sort(models.Select(m => m.ToContract()));
    }

    public async Task<List<AiModel>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var models = await db.AiModels.AsNoTracking().Where(m => m.IsEnabled).ToListAsync(cancellationToken);
        return Sort(models.Select(m => m.ToContract()));
    }

    public async Task<AiModel> SaveAsync(AiModel model, CancellationToken cancellationToken = default)
    {
        model.Name = model.Name.Trim();
        model.ModelId = model.ModelId.Trim();
        model.Tier = string.IsNullOrWhiteSpace(model.Tier) ? AiModel.DefaultTier : model.Tier.Trim();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.AiModels.AnyAsync(m => m.Id != model.Id && m.Name == model.Name && m.Tier == model.Tier, cancellationToken))
        {
            throw new InvalidOperationException($"A model '{model.Name}' with tier '{model.Tier}' already exists.");
        }

        model.UpdatedAt = timeProvider.GetUtcNow();
        var entity = model.ToEntity();
        if (model.Id == 0)
        {
            db.AiModels.Add(entity);
        }
        else
        {
            db.AiModels.Update(entity);
        }
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToContract();
    }

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.AiModels.FindAsync([id], cancellationToken);
        if (model is not null)
        {
            model.IsEnabled = enabled;
            model.UpdatedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.AiModels.FindAsync([id], cancellationToken);
        if (model is not null)
        {
            model.IsFavorite = isFavorite;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.AiModels.FindAsync([id], cancellationToken);
        if (model is not null)
        {
            db.AiModels.Remove(model);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Adds new models and refreshes pricing details of existing ones (matched on name and tier).
    /// The model id and enabled state of existing models are kept, because those can be edited by the user.
    /// </summary>
    public async Task<ModelImportResult> ImportAsync(IEnumerable<AiModel> scraped, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.AiModels.ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        int added = 0, updated = 0, unchanged = 0;

        foreach (var model in scraped)
        {
            var match = existing.FirstOrDefault(m =>
                string.Equals(m.Name, model.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.Tier, model.Tier, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                var entity = model.ToEntity();
                entity.Id = 0;
                entity.UpdatedAt = now;
                db.AiModels.Add(entity);
                existing.Add(entity);
                added++;
                continue;
            }

            match.Provider = model.Provider;
            match.ReleaseStatus = model.ReleaseStatus;
            match.Category = model.Category;
            match.Threshold = model.Threshold;
            match.InputPrice = model.InputPrice;
            match.CachedInputPrice = model.CachedInputPrice;
            match.CacheWritePrice = model.CacheWritePrice;
            match.OutputPrice = model.OutputPrice;
            match.SourceUrl = model.SourceUrl;

            if (db.Entry(match).State == EntityState.Modified)
            {
                match.UpdatedAt = now;
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ModelImportResult(added, updated, unchanged);
    }

    private static List<AiModel> Sort(IEnumerable<AiModel> models) => models
        .OrderBy(m => m.Provider ?? "\uffff", StringComparer.OrdinalIgnoreCase)
        .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(m => m.IsDefaultTier ? 0 : 1)
        .ThenBy(m => m.Tier, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public class AiModelStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IAiModelStore
{
    private static readonly string[] UsageTotals =
    [
        nameof(Entities.AiModel.UsageSessions), nameof(Entities.AiModel.UsageNanoAiu), nameof(Entities.AiModel.UsageRequests),
        nameof(Entities.AiModel.UsageInputTokens), nameof(Entities.AiModel.UsageOutputTokens), nameof(Entities.AiModel.UsageCacheReadTokens),
        nameof(Entities.AiModel.UsageCacheWriteTokens), nameof(Entities.AiModel.UsageReasoningTokens)
    ];

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

    public async Task<AiModel?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return (await db.AiModels.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken))?.ToContract();
    }

    public async Task<PagedResult<AiModelUsage>> GetUsagePageAsync(AiModelUsageQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var usages = db.AgentRunModelUsages.AsNoTracking().Where(u => u.AiModelId == query.ModelId);
        var total = await usages.CountAsync(cancellationToken);
        var page = await usages
            .Join(db.AgentRuns.AsNoTracking(), u => u.AgentRunId, r => r.Id, (u, r) => new { Usage = u, Run = r })
            .OrderByDescending(x => x.Usage.SysId)
            .Skip(query.Page * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        // Runs refer to their work item without a foreign key: a deleted item leaves its runs.
        var ids = page.Select(x => x.Run.WorkItemId).Distinct().ToList();
        var items = (await db.WorkItems.AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .Select(e => new { Item = e, e.Repository.FullName })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => (x.Item.Id, x.FullName), x => x.Item.ToReference());
        return new(page.Select(x => x.Usage.ToContract(x.Run, items.GetValueOrDefault((x.Run.WorkItemId, x.Run.RepositoryFullName)))).ToList(), total);
    }

    public async Task<int> CountUsageSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.AgentRunModelUsages.AsNoTracking()
            .Where(u => u.AiModelId != null)
            .Select(u => new { u.AgentRunId, u.Attempt })
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task<AiModel> SaveAsync(AiModel model, CancellationToken cancellationToken = default)
    {
        model.Name = model.Name.Trim();
        model.ModelId = model.ModelId.Trim();
        model.Tier = string.IsNullOrWhiteSpace(model.Tier) ? AiModel.DefaultTier : model.Tier.Trim();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.AiModels.AnyAsync(m => m.Id != model.Id && m.Name == model.Name && m.Tier == model.Tier, cancellationToken))
        {
            throw new DomainRuleException($"A model '{model.Name}' with tier '{model.Tier}' already exists.");
        }

        model.UpdatedAt = timeProvider.GetUtcNow();
        var entity = model.ToEntity();
        if (model.Id == Guid.Empty)
        {
            db.AiModels.Add(entity);
        }
        else
        {
            db.AiModels.Update(entity);
            // The running totals belong to the agent worker's sessions, which may add to them meanwhile.
            foreach (var total in UsageTotals)
            {
                db.Entry(entity).Property(total).IsModified = false;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        if (model.Id != Guid.Empty)
        {
            return (await db.AiModels.AsNoTracking().SingleAsync(m => m.Id == entity.Id, cancellationToken)).ToContract();
        }
        return entity.ToContract();
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
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

    public async Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.AiModels.FindAsync([id], cancellationToken);
        if (model is not null)
        {
            model.IsFavorite = isFavorite;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
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
                entity.Id = Guid.Empty;
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

    private static List<AiModel> Sort(IEnumerable<AiModel> models)
    {
        return models
            .OrderBy(m => m.Provider ?? "\uffff", StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.IsDefaultTier ? 0 : 1)
            .ThenBy(m => m.Tier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

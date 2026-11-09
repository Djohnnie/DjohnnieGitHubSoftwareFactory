using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class InternalAgentStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IInternalAgentStore
{
    private const int MaxRecordAttempts = 20;

    public async Task<List<InternalAgent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var agents = await db.InternalAgents.AsNoTracking().Include(a => a.InternalModel).ToListAsync(cancellationToken);
        return agents.Select(a => a.ToContract()).ToList();
    }

    public async Task<InternalAgent?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var agent = await db.InternalAgents.AsNoTracking().Include(a => a.InternalModel).SingleOrDefaultAsync(a => a.Key == key, cancellationToken);
        return agent?.ToContract();
    }

    public async Task<InternalAgent> SaveAsync(InternalAgent value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var agent = await db.InternalAgents.Include(a => a.InternalModel).SingleOrDefaultAsync(a => a.Key == value.Key, cancellationToken)
            ?? throw new DomainRuleException($"The internal agent '{value.Key}' does not exist.");
        agent.Instructions = value.Instructions.Replace("\r\n", "\n").Trim();
        agent.InternalModel = value.ModelId is { } modelId
            ? await db.InternalModels.SingleOrDefaultAsync(m => m.Id == modelId, cancellationToken)
                ?? throw new DomainRuleException("The selected model no longer exists.")
            : null;
        agent.IsEnabled = value.IsEnabled;
        agent.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return agent.ToContract();
    }

    public async Task RecordUsageAsync(string key, string model, ModelUsage usage, InternalAgentUsageSource source, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var agent = await db.InternalAgents.SingleOrDefaultAsync(a => a.Key == key, cancellationToken)
                ?? throw new DomainRuleException($"The internal agent '{key}' does not exist.");
            var price = await db.InternalModels.AsNoTracking().FirstOrDefaultAsync(m => m.Name == model, cancellationToken);
            var inputPrice = price?.InputPricePerMillion ?? 0;
            var outputPrice = price?.OutputPricePerMillion ?? 0;
            var cachedInputPrice = price?.CachedInputPricePerMillion ?? inputPrice;
            var inputCost = Contracts.InternalModels.InternalModel.InputCostOf(usage.InputTokens, usage.CachedInputTokens, inputPrice, cachedInputPrice);
            var outputCost = Contracts.InternalModels.InternalModel.CostOf(usage.OutputTokens, outputPrice);

            db.InternalAgentUsages.Add(new Entities.InternalAgentUsage
            {
                InternalAgentId = agent.Id,
                Source = source,
                Model = model,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                CachedInputTokens = usage.CachedInputTokens,
                ReasoningTokens = usage.ReasoningTokens,
                InputPricePerMillion = inputPrice,
                OutputPricePerMillion = outputPrice,
                CachedInputPricePerMillion = cachedInputPrice,
                InputCost = inputCost,
                OutputCost = outputCost,
                OccurredAt = timeProvider.GetUtcNow()
            });
            agent.RequestCount++;
            agent.TotalInputTokens += usage.InputTokens;
            agent.TotalOutputTokens += usage.OutputTokens;
            agent.TotalCachedInputTokens += usage.CachedInputTokens;
            agent.TotalReasoningTokens += usage.ReasoningTokens;
            agent.TotalInputCost += inputCost;
            agent.TotalOutputCost += outputCost;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (PersistenceException ex) when (ex.InnerException is DbUpdateConcurrencyException && attempt < MaxRecordAttempts)
            {
                // Another request of this agent was saved first; start over from its totals.
            }
        }
    }

    public async Task<List<InternalAgentUsage>> GetRecentUsageAsync(string? key, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var usages = await db.InternalAgentUsages.AsNoTracking()
            .Include(u => u.InternalAgent)
            .Where(u => key == null || u.InternalAgent.Key == key)
            .OrderByDescending(u => u.SysId)
            .Take(count)
            .ToListAsync(cancellationToken);
        return usages.Select(u => u.ToContract()).ToList();
    }

    public async Task<int> EnsureAsync(IReadOnlyList<InternalAgent> defaults, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.InternalAgents.ToDictionaryAsync(a => a.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var added = 0;
        // Only agents that are new get the default model; an existing one keeps what it has.
        var defaultModelId = defaults.All(value => existing.ContainsKey(value.Key))
            ? null
            : await db.InternalModels.Where(m => m.IsDefault).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(cancellationToken);
        foreach (var value in defaults)
        {
            if (existing.TryGetValue(value.Key, out var agent))
            {
                agent.Name = value.Name;
                agent.Description = value.Description;
                agent.Kind = value.Kind;
                continue;
            }

            db.InternalAgents.Add(new Entities.InternalAgent
            {
                Key = value.Key,
                Name = value.Name,
                Description = value.Description,
                Kind = value.Kind,
                Instructions = value.Instructions,
                InternalModelId = defaultModelId,
                IsEnabled = value.IsEnabled,
                CreatedAt = now,
                UpdatedAt = now
            });
            added++;
        }

        // Only metadata changes for existing rows; their timestamps stay so the page shows when the prompt changed.
        await db.SaveChangesAsync(cancellationToken);
        return added;
    }
}

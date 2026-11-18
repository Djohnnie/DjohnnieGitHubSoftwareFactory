using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class AgentRunStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IAgentRunStore
{
    public async Task<AgentRun> CreateAsync(string repositoryFullName, Guid workItemId, string? requestedModel, int maxAttempts, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        if (await db.AgentRuns.AnyAsync(r => r.WorkItemId == workItemId && r.RepositoryFullName == normalized
                && r.Status != AgentRunStatus.Succeeded && r.Status != AgentRunStatus.Failed && r.Status != AgentRunStatus.Cancelled, cancellationToken))
        {
            throw new DomainRuleException("The DGHSF agent worker is already working on this item. Wait for that run to end or cancel it.");
        }

        var entity = new Entities.AgentRun
        {
            Id = Guid.NewGuid(),
            WorkItemId = workItemId,
            RepositoryFullName = normalized,
            Status = AgentRunStatus.Pending,
            MaxAttempts = Math.Max(1, maxAttempts),
            RequestedModel = Truncate(requestedModel, 100),
            UsageComplete = true,
            CreatedAt = timeProvider.GetUtcNow()
        };
        db.AgentRuns.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToContract();
    }

    public async Task<IReadOnlyList<Guid>> ClaimDueAsync(int max, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var candidates = await db.AgentRuns
            .Where(r => r.Status == AgentRunStatus.Pending)
            .OrderBy(r => r.SysId)
            .Select(r => r.Id)
            .Take(max)
            .ToListAsync(cancellationToken);

        var claimed = new List<Guid>();
        foreach (var id in candidates)
        {
            var updated = await db.AgentRuns
                .Where(r => r.Id == id && r.Status == AgentRunStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, AgentRunStatus.Queued)
                    .SetProperty(r => r.QueuedAt, now), cancellationToken);
            if (updated == 1)
            {
                claimed.Add(id);
            }
        }
        return claimed;
    }

    public async Task<int> RecoverAsync(TimeSpan queuedTimeout, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var queuedBefore = now - queuedTimeout;

        // The dispatcher marked these queued, but no worker picked them up (the message got lost): queue them again.
        var changed = await db.AgentRuns
            .Where(r => r.Status == AgentRunStatus.Queued && r.QueuedAt < queuedBefore)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, AgentRunStatus.Pending), cancellationToken);

        var expired = await db.AgentRuns.AsNoTracking()
            .Where(r => r.Status == AgentRunStatus.Running && r.LeaseUntil < now)
            .Select(r => new { r.Id, r.Attempts, r.MaxAttempts })
            .ToListAsync(cancellationToken);
        foreach (var run in expired)
        {
            var retry = run.Attempts < run.MaxAttempts;
            var error = $"The worker stopped before the run finished (attempt {run.Attempts} of {run.MaxAttempts}).";
            changed += await db.AgentRuns
                .Where(r => r.Id == run.Id && r.Status == AgentRunStatus.Running && r.LeaseUntil < now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, retry ? AgentRunStatus.Pending : AgentRunStatus.Failed)
                    .SetProperty(r => r.Error, error)
                    .SetProperty(r => r.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(r => r.CompletedAt, retry ? null : now), cancellationToken);
        }
        return changed;
    }

    public async Task<AgentRun?> TryStartAsync(Guid id, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + lease;
        var updated = await db.AgentRuns
            .Where(r => r.Id == id && r.Status == AgentRunStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, AgentRunStatus.Running)
                .SetProperty(r => r.StartedAt, now)
                .SetProperty(r => r.CompletedAt, (DateTimeOffset?)null)
                .SetProperty(r => r.LeaseUntil, leaseUntil)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1), cancellationToken);
        if (updated == 0)
        {
            return null;
        }

        var entity = await db.AgentRuns.AsNoTracking().SingleAsync(r => r.Id == id, cancellationToken);
        return entity.ToContract();
    }

    public async Task<bool> RenewLeaseAsync(Guid id, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var leaseUntil = timeProvider.GetUtcNow() + lease;
        return await db.AgentRuns
            .Where(r => r.Id == id && r.Status == AgentRunStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LeaseUntil, leaseUntil), cancellationToken) == 1;
    }

    public async Task<bool> CompleteAsync(Guid id, AgentRunResult result, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var usage = result.Usage;
        var branch = Truncate(result.Branch, 250);
        var url = Truncate(result.PullRequestUrl, 500);
        var summary = Truncate(result.Summary, AgentRun.MaxSummaryLength);
        var models = Truncate(usage.Models, 500);
        var warning = Truncate(result.Warning, AgentRun.MaxErrorLength);
        return await db.AgentRuns
            .Where(r => r.Id == id && r.Status == AgentRunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, AgentRunStatus.Succeeded)
                .SetProperty(r => r.Branch, branch)
                .SetProperty(r => r.PullRequestNumber, result.PullRequestNumber)
                .SetProperty(r => r.PullRequestUrl, url)
                .SetProperty(r => r.Summary, summary)
                .SetProperty(r => r.Error, warning)
                .SetProperty(r => r.NanoAiu, r => (r.NanoAiu ?? 0) + usage.NanoAiu)
                .SetProperty(r => r.UsageComplete, r => r.UsageComplete && usage.Complete)
                .SetProperty(r => r.PremiumRequests, r => (r.PremiumRequests ?? 0) + (usage.PremiumRequests ?? 0))
                .SetProperty(r => r.InputTokens, r => r.InputTokens + usage.InputTokens)
                .SetProperty(r => r.OutputTokens, r => r.OutputTokens + usage.OutputTokens)
                .SetProperty(r => r.CacheReadTokens, r => r.CacheReadTokens + usage.CacheReadTokens)
                .SetProperty(r => r.CacheWriteTokens, r => r.CacheWriteTokens + usage.CacheWriteTokens)
                .SetProperty(r => r.Models, models)
                .SetProperty(r => r.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(r => r.CompletedAt, now), cancellationToken) == 1;
    }

    public async Task<AgentRunStatus?> FailAsync(Guid id, string error, AgentRunUsage? usage, bool retry, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var run = await db.AgentRuns.AsNoTracking()
            .Where(r => r.Id == id && r.Status == AgentRunStatus.Running)
            .Select(r => new { r.Attempts, r.MaxAttempts })
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return null;
        }

        var status = retry && run.Attempts < run.MaxAttempts ? AgentRunStatus.Pending : AgentRunStatus.Failed;
        DateTimeOffset? completedAt = status == AgentRunStatus.Failed ? now : null;
        var text = Truncate(error, AgentRun.MaxErrorLength);
        // Usage adds up: a retried run that already ran the agent cost something too.
        var spent = usage ?? AgentRunUsage.None;
        var hasUsage = usage is not null;
        var models = Truncate(usage?.Models, 500);
        var updated = await db.AgentRuns
            .Where(r => r.Id == id && r.Status == AgentRunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.Error, text)
                .SetProperty(r => r.NanoAiu, r => hasUsage ? (r.NanoAiu ?? 0) + spent.NanoAiu : r.NanoAiu)
                .SetProperty(r => r.UsageComplete, r => r.UsageComplete && spent.Complete)
                .SetProperty(r => r.PremiumRequests, r => hasUsage ? (r.PremiumRequests ?? 0) + (spent.PremiumRequests ?? 0) : r.PremiumRequests)
                .SetProperty(r => r.InputTokens, r => r.InputTokens + spent.InputTokens)
                .SetProperty(r => r.OutputTokens, r => r.OutputTokens + spent.OutputTokens)
                .SetProperty(r => r.CacheReadTokens, r => r.CacheReadTokens + spent.CacheReadTokens)
                .SetProperty(r => r.CacheWriteTokens, r => r.CacheWriteTokens + spent.CacheWriteTokens)
                .SetProperty(r => r.Models, r => models ?? r.Models)
                .SetProperty(r => r.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(r => r.CompletedAt, completedAt), cancellationToken);
        return updated == 1 ? status : null;
    }

    public async Task<bool> CancelAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        return await db.AgentRuns
            .Where(r => r.Id == id && r.RepositoryFullName == normalized)
            .Where(r => r.Status != AgentRunStatus.Succeeded && r.Status != AgentRunStatus.Failed && r.Status != AgentRunStatus.Cancelled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, AgentRunStatus.Cancelled)
                .SetProperty(r => r.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(r => r.CompletedAt, now), cancellationToken) == 1;
    }

    public async Task<AgentRun?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return (await db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken))?.ToContract();
    }

    public async Task<List<AgentRun>> GetForWorkItemAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var runs = await db.AgentRuns.AsNoTracking()
            .Where(r => r.WorkItemId == workItemId && r.RepositoryFullName == normalized)
            .OrderByDescending(r => r.SysId)
            .ToListAsync(cancellationToken);
        return runs.Select(r => r.ToContract()).ToList();
    }

    public async Task<List<AgentRunOverview>> GetForRepositoryAsync(string repositoryFullName, int max, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var runs = await db.AgentRuns.AsNoTracking()
            .Where(r => r.RepositoryFullName == normalized)
            .OrderByDescending(r => r.SysId)
            .Take(max)
            .ToListAsync(cancellationToken);
        // No foreign key: a run keeps its work item's id after the item was deleted.
        var ids = runs.Select(r => r.WorkItemId).Distinct().ToList();
        var items = await db.WorkItems.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.Repository.FullName == normalized)
            .ToDictionaryAsync(e => e.Id, cancellationToken);
        return runs.Select(r => new AgentRunOverview(r.ToContract(), items.GetValueOrDefault(r.WorkItemId)?.ToReference())).ToList();
    }

    public async Task<AgentRun?> GetLatestAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var run = await db.AgentRuns.AsNoTracking()
            .Where(r => r.WorkItemId == workItemId && r.RepositoryFullName == normalized && r.Status != AgentRunStatus.Cancelled)
            .OrderByDescending(r => r.SysId)
            .FirstOrDefaultAsync(cancellationToken);
        return run?.ToContract();
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
    }
}

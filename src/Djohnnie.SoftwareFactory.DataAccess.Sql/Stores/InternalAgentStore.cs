using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class InternalAgentStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IInternalAgentStore
{
    public async Task<List<InternalAgent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var agents = await db.InternalAgents.AsNoTracking().ToListAsync(cancellationToken);
        return agents.Select(a => a.ToContract()).ToList();
    }

    public async Task<InternalAgent?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var agent = await db.InternalAgents.AsNoTracking().SingleOrDefaultAsync(a => a.Key == key, cancellationToken);
        return agent?.ToContract();
    }

    public async Task<InternalAgent> SaveAsync(InternalAgent value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var agent = await db.InternalAgents.SingleOrDefaultAsync(a => a.Key == value.Key, cancellationToken)
            ?? throw new InvalidOperationException($"The internal agent '{value.Key}' does not exist.");
        agent.Instructions = value.Instructions.Replace("\r\n", "\n").Trim();
        agent.IsEnabled = value.IsEnabled;
        agent.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return agent.ToContract();
    }

    public async Task<int> EnsureAsync(IReadOnlyList<InternalAgent> defaults, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.InternalAgents.ToDictionaryAsync(a => a.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var added = 0;
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

using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

/// <summary>Local data about the custom agents of a repository. The agent files themselves live on GitHub.</summary>
public sealed class AgentStore(IDbContextFactory<AppDbContext> dbFactory) : IAgentStore
{
    /// <summary>
    /// Makes the local agent rows of a repository match the agent files found on GitHub: adds rows for new
    /// agents and removes rows of agents that no longer exist. Returns the file names of the favorite agents.
    /// </summary>
    public async Task<HashSet<string>> SyncAsync(string repositoryFullName, IEnumerable<string> fileNames, CancellationToken cancellationToken = default)
    {
        var present = fileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await db.GetOrAddRepositoryAsync(repositoryFullName, cancellationToken);
        var agents = repository.Id == 0
            ? []
            : await db.Agents.Where(a => a.RepositoryId == repository.Id).ToListAsync(cancellationToken);

        db.Agents.RemoveRange(agents.Where(a => !present.Contains(a.FileName)));
        foreach (var fileName in present.Where(f => !agents.Any(a => string.Equals(a.FileName, f, StringComparison.OrdinalIgnoreCase))))
        {
            db.Agents.Add(new Agent { Repository = repository, FileName = fileName });
        }
        await db.SaveChangesAsync(cancellationToken);

        return agents.Where(a => a.IsFavorite && present.Contains(a.FileName))
            .Select(a => a.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetFavoriteAsync(string repositoryFullName, string fileName, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await db.GetOrAddRepositoryAsync(repositoryFullName, cancellationToken);
        var agent = repository.Id == 0
            ? null
            : await db.Agents.SingleOrDefaultAsync(a => a.RepositoryId == repository.Id && a.FileName == fileName, cancellationToken);
        if (agent is null)
        {
            agent = new Agent { Repository = repository, FileName = fileName };
            db.Agents.Add(agent);
        }
        agent.IsFavorite = isFavorite;
        await db.SaveChangesAsync(cancellationToken);
    }
}

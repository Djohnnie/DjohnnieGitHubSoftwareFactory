using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>
/// Deletes the snapshots of runs that have ended (or no longer exist). The Tasks function app calls it on a timer; a
/// lifecycle rule on the container is the safety net for anything this misses.
/// </summary>
public sealed class RepositorySnapshotCleaner(IRepositorySnapshotStorage storage, ITodoTaskStore tasks, ILogger<RepositorySnapshotCleaner> logger)
{
    /// <summary>Returns how many snapshots were deleted.</summary>
    public async Task<int> CleanUpAsync(CancellationToken cancellationToken = default)
    {
        if (!storage.IsConfigured)
        {
            return 0;
        }

        var deleted = 0;
        foreach (var runId in await storage.GetRunsAsync(cancellationToken))
        {
            var status = await tasks.GetStatusAsync(runId, cancellationToken);
            if (status is { IsFinal: false })
            {
                continue;
            }

            await storage.DeleteAsync(runId, cancellationToken);
            deleted++;
            logger.LogInformation("Deleted the repository snapshot of run {RunId} ({Status}).", runId, status?.ToString() ?? "deleted");
        }
        return deleted;
    }
}

using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Sync;

public sealed class WorkItemSyncFunction(WorkItemSyncService sync, ILogger<WorkItemSyncFunction> logger)
{
    /// <summary>
    /// Runs on the CRON expression in the <c>WorkItemSyncSchedule</c> app setting (six fields, with seconds, in UTC).
    /// The sync writes what it does to the sync log in SQL (shown in DGHSF) and to the function's logs; a run that
    /// stopped early (refused token, rate limit) or couldn't start throws, so it shows as failed.
    /// </summary>
    [Function("WorkItemSync")]
    public async Task RunAsync([TimerTrigger("%WorkItemSyncSchedule%")] TimerInfo timer, CancellationToken cancellationToken)
    {
        var result = await sync.SyncAcceptedAsync(cancellationToken);
        logger.LogInformation("Synced {Items} accepted work items of {Repositories} repositories: {Changed} changed, {Failed} failed, {ProgressWarnings} without (all of) Copilot's progress.",
            result.Items, result.Repositories, result.Changed, result.Failures.Count, result.ProgressWarnings);
    }
}

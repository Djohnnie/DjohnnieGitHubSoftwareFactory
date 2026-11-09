using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Sync;

public sealed class WorkItemSyncFunction(WorkItemSyncService sync, ILogger<WorkItemSyncFunction> logger)
{
    /// <summary>Runs on the CRON expression in the <c>WorkItemSyncSchedule</c> app setting (six fields, with seconds, in UTC).</summary>
    [Function("WorkItemSync")]
    public async Task RunAsync([TimerTrigger("%WorkItemSyncSchedule%")] TimerInfo timer, CancellationToken cancellationToken)
    {
        try
        {
            var result = await sync.SyncAcceptedAsync(cancellationToken);
            foreach (var failure in result.Failures)
            {
                logger.LogWarning("Could not sync {Repository} issue #{IssueNumber}: {Error}", failure.RepositoryFullName, failure.IssueNumber, failure.Error);
            }

            logger.LogInformation("Synced {Items} accepted work items of {Repositories} repositories: {Changed} changed, {Failed} failed.",
                result.Items, result.Repositories, result.Changed, result.Failures.Count);
        }
        catch (GitHubApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The client's own message tells a signed-in user to sign in again, which doesn't apply here.
            logger.LogError(ex, "GitHub refused the token in GitHub:SyncToken; it has expired or was revoked.");
            throw;
        }
    }
}

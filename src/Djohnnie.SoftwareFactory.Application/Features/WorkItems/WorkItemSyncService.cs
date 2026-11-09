using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>Refreshes every accepted work item of every known repository from its GitHub issue; nothing is sent to GitHub.</summary>
public sealed class WorkItemSyncService(RepositoryService repositories, WorkItemService workItems)
{
    /// <summary>A problem with one item or repository is reported in the result and doesn't stop the others.</summary>
    /// <exception cref="GitHubApiException">GitHub refused the token or the rate limit was reached, so the other items would fail too.</exception>
    public async Task<WorkItemSyncResult> SyncAcceptedAsync(CancellationToken cancellationToken = default)
    {
        var known = await repositories.GetAllAsync(cancellationToken);
        var failures = new List<WorkItemSyncFailure>();
        var total = 0;
        var changed = 0;

        foreach (var repository in known)
        {
            List<WorkItem> items;
            try
            {
                items = await workItems.GetAllAsync(repository.FullName, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (AffectsOnlyThisOne(ex, cancellationToken))
            {
                failures.Add(new(repository.FullName, null, ex.Message));
                continue;
            }

            foreach (var item in items.Where(i => i.HasIssue))
            {
                total++;
                try
                {
                    var result = await workItems.RefreshAsync(repository.FullName, item.Id, cancellationToken);
                    changed += result.Changed ? 1 : 0;
                }
                catch (Exception ex) when (AffectsOnlyThisOne(ex, cancellationToken))
                {
                    failures.Add(new(repository.FullName, item.IssueNumber, ex.Message));
                }
            }
        }

        return new(known.Count, total, changed, failures);
    }

    private static bool AffectsOnlyThisOne(Exception ex, CancellationToken cancellationToken)
    {
        return ex switch
        {
            GitHubApiException gitHub => !StopsTheRun(gitHub),
            DomainRuleException or ValidationFailedException or PersistenceException or HttpRequestException => true,
            // HttpClient's own timeout, not this run being cancelled.
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false
        };
    }

    private static bool StopsTheRun(GitHubApiException ex)
    {
        // GitHub answers 403 or 429 when the rate limit is reached; a plain 403 only means no access to this repository.
        return ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests
            || (ex.StatusCode == HttpStatusCode.Forbidden && ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
    }
}

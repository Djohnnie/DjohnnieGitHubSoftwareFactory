using System.Data.Common;
using System.Diagnostics;
using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Refreshes every accepted work item of every known repository from its GitHub issue; nothing is sent to GitHub.
/// What it does is written to the sync log (<see cref="ISyncLogStore"/>) and to <see cref="ILogger"/>.
/// </summary>
public sealed class WorkItemSyncService(
    RepositoryService repositories,
    WorkItemService workItems,
    ISyncLogStore log,
    TimeProvider time,
    IOptions<WorkItemSyncOptions> options,
    ILogger<WorkItemSyncService> logger)
{
    /// <summary>How long writing the log may take once the run itself was cancelled (e.g. the host is shutting down).</summary>
    private static readonly TimeSpan LogTimeoutAfterCancellation = TimeSpan.FromSeconds(10);

    /// <summary>
    /// First lists the accepted items of every repository (database only), then refreshes them one by one. A problem with
    /// one item or repository is reported in the result and the log and doesn't stop the others; transient errors are
    /// retried first (<see cref="WorkItemSyncOptions.RetryDelays"/>).
    /// </summary>
    /// <exception cref="GitHubApiException">
    /// GitHub refused the token or the rate limit was reached, so the other items would fail too. The run is logged
    /// with the number of items it skipped.
    /// </exception>
    public async Task<WorkItemSyncResult> SyncAcceptedAsync(CancellationToken cancellationToken = default)
    {
        var run = new Run(Guid.NewGuid(), time);

        IReadOnlyList<RepositorySummary> known;
        try
        {
            known = await repositories.GetAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var cancelled = IsRunCancelled(ex, cancellationToken);
            run.Add(cancelled ? SyncLogLevel.Warning : SyncLogLevel.Error, SyncLogEventKind.RunFailed, cancelled
                ? "The run was cancelled before it started."
                : $"The repositories could not be listed, so nothing was synced: {ex.Message}", detail: cancelled ? null : DetailOf(ex));
            await WriteAsync(run, cancellationToken);
            throw;
        }

        var failures = new List<WorkItemSyncFailure>();
        var eligible = new List<WorkItem>();
        foreach (var repository in known)
        {
            try
            {
                var items = await WithRetriesAsync(() => workItems.GetAllAsync(repository.FullName, cancellationToken: cancellationToken), cancellationToken);
                eligible.AddRange(items.Where(i => i.HasIssue).OrderBy(i => i.IssueNumber));
            }
            catch (Exception ex) when (AffectsOnlyThisOne(ex, cancellationToken))
            {
                failures.Add(new(repository.FullName, null, ex.Message));
                run.Add(SyncLogLevel.Error, SyncLogEventKind.RepositoryFailed,
                    $"The work items of {repository.FullName} could not be listed, so none of them were synced: {ex.Message}",
                    repository.FullName, detail: DetailOf(ex));
            }
            catch (Exception ex) when (IsRunCancelled(ex, cancellationToken))
            {
                run.Add(SyncLogLevel.Warning, SyncLogEventKind.RunFailed, "The run was cancelled while listing the work items; nothing was synced.");
                await WriteAsync(run, cancellationToken);
                throw;
            }
        }

        if (eligible.Count == 0 && failures.Count == 0)
        {
            await RepeatAsync(run, SyncLogEventKind.NoEligibleWorkItems,
                n => $"No action for {Iterations(n)} because no work items are in an eligible state (accepted, with a GitHub issue).", cancellationToken);
            return new(known.Count, 0, 0, failures);
        }

        var changed = 0;
        var progressWarnings = new List<(WorkItem Item, string Warning)>();
        for (var index = 0; index < eligible.Count; index++)
        {
            var item = eligible[index];
            try
            {
                var result = await WithRetriesAsync(() => workItems.RefreshAsync(item.RepositoryFullName, item.Id, cancellationToken), cancellationToken);
                if (result.Changed)
                {
                    changed++;
                    run.Add(SyncLogLevel.Information, SyncLogEventKind.WorkItemChanged, WorkItemChanges.Describe(item, result.WorkItem),
                        item.RepositoryFullName, item.IssueNumber, item.Id);
                }
                if (result.ProgressWarning is { } warning)
                {
                    progressWarnings.Add((item, warning));
                }
            }
            catch (GitHubApiException ex) when (StopsTheRun(ex))
            {
                AddProgressWarnings(run, progressWarnings);
                var skipped = eligible.Count - index;
                run.Add(SyncLogLevel.Error, SyncLogEventKind.RunStopped,
                    $"Stopped at {item.RepositoryFullName} #{item.IssueNumber} after {index} of {Count(eligible.Count, "work item")}, so {skipped} {(skipped == 1 ? "was" : "were")} skipped: {StopReason(ex)}",
                    item.RepositoryFullName, item.IssueNumber, item.Id, $"GitHub answered {(int)ex.StatusCode}: {ex.Message}");
                await WriteAsync(run, cancellationToken);
                throw;
            }
            catch (Exception ex) when (AffectsOnlyThisOne(ex, cancellationToken))
            {
                failures.Add(new(item.RepositoryFullName, item.IssueNumber, ex.Message));
                // A refused rule (e.g. a deleted issue) is about the item; anything else may be a bug or an outage.
                run.Add(ex is DomainRuleException ? SyncLogLevel.Warning : SyncLogLevel.Error, SyncLogEventKind.WorkItemFailed,
                    $"{item.Type.DisplayName} '{item.Title}' could not be synced: {ex.Message}",
                    item.RepositoryFullName, item.IssueNumber, item.Id, DetailOf(ex));
            }
            catch (Exception ex) when (IsRunCancelled(ex, cancellationToken))
            {
                AddProgressWarnings(run, progressWarnings);
                run.Add(SyncLogLevel.Warning, SyncLogEventKind.RunFailed,
                    $"The run was cancelled after {index} of {Count(eligible.Count, "work item")} ({changed} changed).");
                await WriteAsync(run, cancellationToken);
                throw;
            }
        }

        var syncResult = new WorkItemSyncResult(known.Count, eligible.Count, changed, failures, progressWarnings.Count);
        if (changed == 0 && failures.Count == 0 && progressWarnings.Count == 0)
        {
            var repositoryCount = eligible.Select(i => i.RepositoryFullName).Distinct().Count();
            await RepeatAsync(run, SyncLogEventKind.NoChanges,
                n => $"No changes for {Iterations(n)}: {Count(eligible.Count, "work item")} in {Count(repositoryCount, "repository", "repositories")} checked, all up to date.",
                cancellationToken);
            return syncResult;
        }

        AddProgressWarnings(run, progressWarnings);
        run.Add(syncResult.Failures.Count > 0 ? SyncLogLevel.Error : syncResult.ProgressWarnings > 0 ? SyncLogLevel.Warning : SyncLogLevel.Information,
            SyncLogEventKind.RunCompleted, Summary(syncResult, run.Elapsed));
        await WriteAsync(run, cancellationToken);
        return syncResult;
    }

    /// <summary>Retries transient errors; the last error, or any other one, is thrown.</summary>
    private async Task<T> WithRetriesAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        var delays = options.Value.RetryDelays;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < delays.Length && IsTransient(ex, cancellationToken))
            {
                logger.LogInformation(ex, "Retrying after a transient error (attempt {Attempt} of {Attempts}).", attempt + 2, delays.Length + 1);
                await Task.Delay(delays[attempt], time, cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken)
    {
        return ex switch
        {
            GitHubApiException gitHub => gitHub.StatusCode is HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError,
            HttpRequestException or DbException or PersistenceException => true,
            // HttpClient's own timeout, not this run being cancelled.
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false
        };
    }

    /// <summary>Everything except a cancelled run and the GitHub errors that would fail every next request.</summary>
    private static bool AffectsOnlyThisOne(Exception ex, CancellationToken cancellationToken)
    {
        return ex switch
        {
            GitHubApiException gitHub => !StopsTheRun(gitHub),
            _ => !IsRunCancelled(ex, cancellationToken)
        };
    }

    private static bool IsRunCancelled(Exception ex, CancellationToken cancellationToken)
    {
        return ex is OperationCanceledException && cancellationToken.IsCancellationRequested;
    }

    private static bool StopsTheRun(GitHubApiException ex)
    {
        // GitHub answers 403 or 429 when the rate limit is reached; a plain 403 only means no access to this repository.
        return ex.StatusCode == HttpStatusCode.Unauthorized || IsRateLimit(ex);
    }

    private static bool IsRateLimit(GitHubApiException ex)
    {
        return ex.StatusCode == HttpStatusCode.TooManyRequests
            || (ex.StatusCode == HttpStatusCode.Forbidden && ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
    }

    private static string StopReason(GitHubApiException ex)
    {
        return IsRateLimit(ex)
            ? "GitHub's rate limit for the sync token was reached; the next run continues once it resets."
            : "GitHub refused the token in GitHub:SyncToken; it has expired or was revoked.";
    }

    /// <summary>The exception type and stack for an unexpected error; nothing for the errors whose message says it all.</summary>
    private static string? DetailOf(Exception ex)
    {
        return ex is DomainRuleException or ValidationFailedException or GitHubApiException ? null : ex.ToString();
    }

    private static string Iterations(int count)
    {
        return Count(count, "iteration");
    }

    private static string Count(int count, string singular, string? plural = null)
    {
        return count == 1 ? $"1 {singular}" : $"{count:N0} {plural ?? singular + "s"}";
    }

    private static string Summary(WorkItemSyncResult result, TimeSpan elapsed)
    {
        var parts = new List<string> { $"{result.Changed} changed" };
        if (result.Failures.Count > 0)
        {
            parts.Add($"{result.Failures.Count} failed");
        }
        if (result.ProgressWarnings > 0)
        {
            parts.Add($"{result.ProgressWarnings} without (all of) Copilot's progress");
        }
        return $"Synced {Count(result.Items, "work item")} of {Count(result.Repositories, "repository", "repositories")} in {elapsed.TotalSeconds:N1} s: {string.Join(", ", parts)}.";
    }

    /// <summary>One warning per repository and reason, listing its items, rather than one per item.</summary>
    private static void AddProgressWarnings(Run run, List<(WorkItem Item, string Warning)> warnings)
    {
        foreach (var group in warnings.GroupBy(w => (w.Item.RepositoryFullName, w.Warning)))
        {
            var issues = string.Join(", ", group.Select(w => $"#{w.Item.IssueNumber}"));
            var count = group.Count();
            run.Add(SyncLogLevel.Warning, SyncLogEventKind.ProgressUnavailable,
                $"{count} work {(count == 1 ? "item was" : "items were")} synced without (all of) Copilot's progress ({issues}): {group.Key.Warning}",
                group.Key.RepositoryFullName, count == 1 ? group.First().Item.IssueNumber : null, count == 1 ? group.First().Item.Id : null);
        }
        warnings.Clear();
    }

    private async Task RepeatAsync(Run run, SyncLogEventKind kind, Func<int, string> messageFor, CancellationToken cancellationToken)
    {
        var entry = run.Create(SyncLogLevel.Information, kind, messageFor(1));
        logger.LogInformation("{Message}", entry.Message);
        try
        {
            await log.AddOrRepeatAsync(entry, messageFor, cancellationToken);
        }
        catch (Exception ex)
        {
            // The log must never fail the sync it describes.
            logger.LogError(ex, "Could not write to the sync log.");
        }
    }

    /// <summary>Writes the run's events, also when the run was cancelled; a failure to write is only logged.</summary>
    private async Task WriteAsync(Run run, CancellationToken cancellationToken)
    {
        foreach (var entry in run.Events)
        {
            logger.Log(entry.Level switch
            {
                SyncLogLevel.Error => LogLevel.Error,
                SyncLogLevel.Warning => LogLevel.Warning,
                _ => LogLevel.Information
            }, "{Message} {Repository} {IssueNumber}", entry.Message, entry.RepositoryFullName, entry.IssueNumber);
        }

        using var timeout = cancellationToken.IsCancellationRequested ? new CancellationTokenSource(LogTimeoutAfterCancellation, time) : null;
        try
        {
            await log.AddAsync(run.Events, timeout?.Token ?? cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write {Count} events to the sync log.", run.Events.Count);
        }
    }

    /// <summary>The events of one run, each stamped with the time it happened.</summary>
    private sealed class Run(Guid id, TimeProvider time)
    {
        private readonly long _started = time.GetTimestamp();

        public List<SyncLogEvent> Events { get; } = [];

        public TimeSpan Elapsed
        {
            get
            {
                return time.GetElapsedTime(_started);
            }
        }

        public void Add(SyncLogLevel level, SyncLogEventKind kind, string message, string? repositoryFullName = null, int? issueNumber = null,
            Guid? workItemId = null, string? detail = null)
        {
            Events.Add(Create(level, kind, message, repositoryFullName, issueNumber, workItemId, detail));
        }

        public SyncLogEvent Create(SyncLogLevel level, SyncLogEventKind kind, string message, string? repositoryFullName = null, int? issueNumber = null,
            Guid? workItemId = null, string? detail = null)
        {
            // Each event gets its own moment, so the newest-first page keeps the order they happened in.
            var now = time.GetUtcNow();
            if (Events.Count > 0 && now <= Events[^1].OccurredAt)
            {
                now = Events[^1].OccurredAt.AddTicks(1);
            }
            return new(Guid.NewGuid(), id, now, now, 1, level, kind, repositoryFullName, issueNumber, workItemId, message, detail);
        }
    }
}

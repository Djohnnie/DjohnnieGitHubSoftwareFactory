using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.Application.Features.SyncLog;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>The sync's resilience and what it writes to the sync log.</summary>
public partial class WorkItemServiceTests
{
    private const string SyncedIssuePath = "/repos/owner/repo/issues/42";

    [Fact]
    public async Task Sync_LogsEveryChangedItemAndASummaryOfTheRun()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.IssuesOnGitHub[42] = new("Renamed", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        _gitHub.IssuesOnGitHub[43] = new("Task", $"About Task.\n\n{WorkItemWorkflow.Marker(task)}", "closed", "Copilot");

        await Sync.SyncAcceptedAsync(_cancellationToken);

        var log = await SyncLogAsync();
        Assert.Equal([SyncLogEventKind.RunCompleted, SyncLogEventKind.WorkItemChanged, SyncLogEventKind.WorkItemChanged], log.Select(e => e.Kind));
        Assert.All(log, e => Assert.Equal(log[0].RunId, e.RunId));
        Assert.Equal(SyncLogLevel.Information, log[0].Level);
        Assert.StartsWith("Synced 2 work items of 1 repository in ", log[0].Message);
        Assert.EndsWith(": 2 changed.", log[0].Message);

        var renamed = Assert.Single(log, e => e.IssueNumber == 42);
        Assert.Equal("Epic 'Renamed': title 'Epic' → 'Renamed'.", renamed.Message);
        Assert.Equal((Repo, epic.Id, "https://github.com/owner/repo/issues/42"), (renamed.RepositoryFullName, renamed.WorkItemId, renamed.IssueUrl));
        Assert.Contains("status In progress → Closed", Assert.Single(log, e => e.IssueNumber == 43).Message);
    }

    [Fact]
    public async Task Sync_WithoutEligibleWorkItems_KeepsOneLineThatCountsTheIterations()
    {
        await SaveAsync(WorkItemType.Bug, "Only a draft");

        for (var run = 0; run < 3; run++)
        {
            await Sync.SyncAcceptedAsync(_cancellationToken);
        }

        var only = Assert.Single(await SyncLogAsync());
        Assert.Equal((SyncLogEventKind.NoEligibleWorkItems, SyncLogLevel.Information, 3), (only.Kind, only.Level, only.Iterations));
        Assert.Equal("No action for 3 iterations because no work items are in an eligible state (accepted, with a GitHub issue).", only.Message);
        Assert.True(only.LastOccurredAt >= only.OccurredAt);
    }

    [Fact]
    public async Task Sync_WithoutChanges_CollapsesTheRunsUntilSomethingHappens()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new("Epic", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);
        _gitHub.IssuesOnGitHub[42] = new("Renamed", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        await Sync.SyncAcceptedAsync(_cancellationToken);

        await Sync.SyncAcceptedAsync(_cancellationToken);

        var log = await SyncLogAsync();
        Assert.Equal(
            [SyncLogEventKind.NoChanges, SyncLogEventKind.RunCompleted, SyncLogEventKind.WorkItemChanged, SyncLogEventKind.NoChanges],
            log.Select(e => e.Kind));
        Assert.Equal([1, 1, 1, 2], log.Select(e => e.Iterations));
        Assert.Equal("No changes for 2 iterations: 1 work item in 1 repository checked, all up to date.", log[3].Message);
        Assert.Equal("No changes for 1 iteration: 1 work item in 1 repository checked, all up to date.", log[0].Message);
    }

    [Fact]
    public async Task Sync_RetriesATransientGitHubError()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new("Renamed", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        _gitHub.TransientIssueFailures = 2;
        _gitHub.Requests.Clear();

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(result.Failures);
        Assert.Equal(1, result.Changed);
        Assert.Equal(3, _gitHub.Requests.Count(r => r.Method == "GET" && r.Path == SyncedIssuePath));
    }

    [Fact]
    public async Task Sync_WhenATransientErrorPersists_ReportsTheItemAfterTheRetriesAndSyncsTheOthers()
    {
        await AcceptAsync(WorkItemType.Epic, "Down");
        var kept = await AcceptAsync(WorkItemType.Epic, "Kept");
        _gitHub.IssuesOnGitHub[43] = new("Renamed", $"About Kept.\n\n{WorkItemWorkflow.Marker(kept)}", "open");
        _gitHub.TransientIssueFailures = 3;
        _gitHub.Requests.Clear();

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(42, Assert.Single(result.Failures).IssueNumber);
        Assert.Equal(1, result.Changed);
        Assert.Equal(3, _gitHub.Requests.Count(r => r.Method == "GET" && r.Path == SyncedIssuePath));
        var failed = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.WorkItemFailed);
        Assert.Equal((SyncLogLevel.Error, 42), (failed.Level, failed.IssueNumber));
        Assert.Contains("Server Error", failed.Message);
    }

    [Fact]
    public async Task Sync_WhenAnItemFailsUnexpectedly_LogsTheErrorWithoutRetryingAndSyncsTheOthers()
    {
        await AcceptAsync(WorkItemType.Epic, "Broken");
        var kept = await AcceptAsync(WorkItemType.Epic, "Kept");
        _gitHub.MalformedIssues.Add(42);
        _gitHub.IssuesOnGitHub[43] = new("Renamed", $"About Kept.\n\n{WorkItemWorkflow.Marker(kept)}", "open");
        _gitHub.Requests.Clear();

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(42, Assert.Single(result.Failures).IssueNumber);
        Assert.Equal("Renamed", (await _service.GetAsync(Repo, kept.Id, _cancellationToken))!.Title);
        Assert.Single(_gitHub.Requests, r => r.Method == "GET" && r.Path == SyncedIssuePath);
        var log = await SyncLogAsync();
        var failed = Assert.Single(log, e => e.Kind == SyncLogEventKind.WorkItemFailed);
        Assert.Equal(SyncLogLevel.Error, failed.Level);
        Assert.Contains("JsonException", failed.Detail);
        Assert.Equal((SyncLogEventKind.RunCompleted, SyncLogLevel.Error), (log[0].Kind, log[0].Level));
        Assert.Contains("1 changed, 1 failed", log[0].Message);
    }

    [Fact]
    public async Task Sync_WhenAnIssueIsGone_LogsAWarningForThatItem()
    {
        await AcceptAsync(WorkItemType.Epic, "Gone");
        _gitHub.IssuesOnGitHub[42] = null;

        await Sync.SyncAcceptedAsync(_cancellationToken);

        var failed = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.WorkItemFailed);
        Assert.Equal(SyncLogLevel.Warning, failed.Level);
        Assert.Null(failed.Detail);
        Assert.Contains("#42 no longer exists", failed.Message);
    }

    [Fact]
    public async Task Sync_WithoutAccessToThePullRequests_WarnsOnceForTheRepository()
    {
        await AcceptAsync(WorkItemType.Task, "One");
        await AcceptAsync(WorkItemType.Task, "Two");
        _gitHub.GraphQlFailure = (HttpStatusCode.Forbidden, "Resource not accessible by personal access token");

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.ProgressWarnings);
        var log = await SyncLogAsync();
        var warning = Assert.Single(log, e => e.Kind == SyncLogEventKind.ProgressUnavailable);
        Assert.Equal((SyncLogLevel.Warning, Repo, (int?)null), (warning.Level, warning.RepositoryFullName, warning.IssueNumber));
        Assert.StartsWith("2 work items were synced without (all of) Copilot's progress (#42, #43): ", warning.Message);
        Assert.Contains("Resource not accessible by personal access token", warning.Message);
        Assert.Equal((SyncLogEventKind.RunCompleted, SyncLogLevel.Warning), (log[0].Kind, log[0].Level));
    }

    [Fact]
    public async Task Sync_WithoutAccessToActions_WarnsThatTheAgentRunIsUnknown()
    {
        await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.LinkedPullRequests[42] = [new(7, "[WIP] Task", "OPEN", "copilot/fix-42")];
        _gitHub.RunsFailure = HttpStatusCode.Forbidden;

        await Sync.SyncAcceptedAsync(_cancellationToken);

        var warning = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.ProgressUnavailable);
        Assert.Equal(42, warning.IssueNumber);
        Assert.EndsWith(WorkItemWorkflow.ActionsRefusedWarning, warning.Message);
    }

    [Fact]
    public async Task Sync_WhenTheLogCantBeWritten_StillSyncs()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new("Renamed", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        var sync = new WorkItemSyncService(_services.GetRequiredService<RepositoryService>(), _service, new BrokenSyncLog(), TimeProvider.System,
            Options.Create(new WorkItemSyncOptions { RetryDelays = [] }), NullLogger<WorkItemSyncService>.Instance);

        var result = await sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(1, result.Changed);
        Assert.Equal("Renamed", (await _service.GetAsync(Repo, epic.Id, _cancellationToken))!.Title);
    }

    [Fact]
    public async Task Sync_WhenCancelled_StillLogsIt()
    {
        await AcceptAsync(WorkItemType.Epic, "Epic");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sync.SyncAcceptedAsync(cancelled.Token));

        var failed = Assert.Single(await SyncLogAsync());
        Assert.Equal(SyncLogEventKind.RunFailed, failed.Kind);
        Assert.Contains("cancelled", failed.Message);
    }

    private async Task<IReadOnlyList<SyncLogEvent>> SyncLogAsync()
    {
        var page = await _services.GetRequiredService<SyncLogService>().GetPageAsync(new SyncLogQuery(0, SyncLogQuery.MaxPageSize), _cancellationToken);
        return page.Items;
    }

    private sealed class BrokenSyncLog : ISyncLogStore
    {
        public Task AddAsync(IReadOnlyList<SyncLogEvent> events, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The database is down.");
        }

        public Task AddOrRepeatAsync(SyncLogEvent entry, Func<int, string> messageFor, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The database is down.");
        }

        public Task<PagedResult<SyncLogEvent>> GetPageAsync(SyncLogQuery query, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The database is down.");
        }

        public Task<List<string>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The database is down.");
        }
    }
}

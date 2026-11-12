using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    private WorkItemSyncService Sync => _services.GetRequiredService<WorkItemSyncService>();

    [Fact]
    public async Task Sync_RefreshesAcceptedItemsAndLeavesDraftsAlone()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        var draft = await SaveAsync(WorkItemType.Bug, "Crash");
        _gitHub.IssuesOnGitHub[42] = new("Renamed", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        _gitHub.IssuesOnGitHub[43] = new("Task", $"About Task.\n\n{WorkItemWorkflow.Marker(task)}", "closed", "Copilot");
        await _services.GetRequiredService<RepositoryService>().RecordAccessAsync("owner/without-items", _cancellationToken);

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal((2, 2, 2), (result.Repositories, result.Items, result.Changed));
        Assert.Empty(result.Failures);
        Assert.Equal("Renamed", (await _service.GetAsync(Repo, epic.Id, _cancellationToken))!.Title);
        Assert.Equal(WorkItemStatus.Done, (await _service.GetAsync(Repo, task.Id, _cancellationToken))!.Status);
        Assert.Equal(draft, await _service.GetAsync(Repo, draft.Id, _cancellationToken));
    }

    [Fact]
    public async Task Sync_AlsoRefreshesClosedItems_SoAReopenedIssueMovesBack()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        var marker = WorkItemWorkflow.Marker(epic);
        _gitHub.IssuesOnGitHub[42] = new("Epic", $"About Epic.\n\n{marker}", "closed");
        await Sync.SyncAcceptedAsync(_cancellationToken);
        _gitHub.IssuesOnGitHub[42] = new("Epic", $"About Epic.\n\n{marker}", "open");

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(WorkItemStatus.Accepted, (await _service.GetAsync(Repo, epic.Id, _cancellationToken))!.Status);
    }

    [Fact]
    public async Task Sync_WhenAnIssueIsGone_ReportsItAndStillSyncsTheOthers()
    {
        var gone = await AcceptAsync(WorkItemType.Epic, "Gone");
        var kept = await AcceptAsync(WorkItemType.Epic, "Kept");
        _gitHub.IssuesOnGitHub[42] = null;
        _gitHub.IssuesOnGitHub[43] = new("Renamed", $"About Kept.\n\n{WorkItemWorkflow.Marker(kept)}", "open");

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        var failure = Assert.Single(result.Failures);
        Assert.Equal((Repo, 42), (failure.RepositoryFullName, failure.IssueNumber));
        Assert.Contains("#42 no longer exists", failure.Error);
        Assert.Equal((2, 1), (result.Items, result.Changed));
        Assert.Equal(gone, await _service.GetAsync(Repo, gone.Id, _cancellationToken));
        Assert.Equal("Renamed", (await _service.GetAsync(Repo, kept.Id, _cancellationToken))!.Title);
    }

    [Fact]
    public async Task Sync_WithoutAccessToOneIssue_ContinuesWithTheOthers()
    {
        await AcceptAsync(WorkItemType.Epic, "Epic");
        await AcceptAsync(WorkItemType.Epic, "Other");
        _gitHub.IssueReadFailure = (HttpStatusCode.Forbidden, "Resource not accessible by personal access token");

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(2, result.Failures.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Bad credentials")]
    [InlineData(HttpStatusCode.Forbidden, "API rate limit exceeded for user")]
    [InlineData(HttpStatusCode.TooManyRequests, "You have exceeded a secondary rate limit")]
    public async Task Sync_WhenGitHubRefusesEveryRequest_StopsAtTheFirstItem(HttpStatusCode status, string message)
    {
        await AcceptAsync(WorkItemType.Epic, "Epic");
        await AcceptAsync(WorkItemType.Epic, "Other");
        _gitHub.Requests.Clear();
        _gitHub.IssueReadFailure = (status, message);

        var error = await Assert.ThrowsAsync<GitHubApiException>(() => Sync.SyncAcceptedAsync(_cancellationToken));

        Assert.Equal(status, error.StatusCode);
        Assert.Single(_gitHub.Requests);
        var stopped = Assert.Single(await SyncLogAsync());
        Assert.Equal((SyncLogEventKind.RunStopped, SyncLogLevel.Error, 42), (stopped.Kind, stopped.Level, stopped.IssueNumber));
        Assert.Contains("after 0 of 2 work items, so 2 were skipped", stopped.Message);
        Assert.Contains(status == HttpStatusCode.Unauthorized ? "refused the token" : "rate limit", stopped.Message);
    }
}

using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    private RepositoryService Repositories => _services.GetRequiredService<RepositoryService>();

    [Fact]
    public async Task Sync_ArchivesARepositoryGitHubNoLongerKnows_AndTakesItOutOfTheFavoritesAndRecent()
    {
        await AcceptAsync(WorkItemType.Epic, "Epic");
        await Repositories.RecordAccessAsync(Repo, _cancellationToken);
        await Repositories.RecordAccessAsync("owner/gone", _cancellationToken);
        await Repositories.SetFavoriteAsync("owner/gone", true, _cancellationToken);
        await Repositories.RecordAccessAsync("owner/stale", _cancellationToken);
        _gitHub.DeletedRepositories.UnionWith(["owner/gone", "owner/stale"]);

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal((3, 1), (result.Repositories, result.Items));
        Assert.Empty(result.Failures);
        Assert.Equal(["owner/gone", "owner/stale"], (await Repositories.GetArchivedAsync(_cancellationToken)).Select(r => r.FullName));
        Assert.All(await Repositories.GetArchivedAsync(_cancellationToken), r => Assert.NotNull(r.ArchivedAt));
        Assert.Empty(await Repositories.GetFavoritesAsync(_cancellationToken));
        Assert.Equal([Repo], (await Repositories.GetRecentAsync(_cancellationToken)).Select(r => r.FullName));
        Assert.False((await Repositories.FindAsync("owner/gone", _cancellationToken))!.IsFavorite);

        var archived = (await SyncLogAsync()).Where(e => e.Kind == SyncLogEventKind.RepositoryArchived).ToList();
        Assert.Equal(["owner/gone", "owner/stale"], archived.Select(e => e.RepositoryFullName).Order());
        Assert.All(archived, e => Assert.Equal(SyncLogLevel.Warning, e.Level));
        Assert.Contains("no longer exists on GitHub", archived[0].Message);
    }

    [Fact]
    public async Task Sync_LeavesTheWorkItemsOfAnArchivedRepositoryAlone_AndArchivesItOnlyOnce()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new("Renamed", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        _gitHub.DeletedRepositories.Add(Repo);
        _gitHub.Requests.Clear();

        var first = await Sync.SyncAcceptedAsync(_cancellationToken);
        var second = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal((1, 0, 0), (first.Repositories, first.Items, first.Changed));
        Assert.Equal((1, 0), (second.Repositories, second.Items));
        Assert.Equal("Epic", (await _service.GetAsync(Repo, epic.Id, _cancellationToken))!.Title);
        Assert.All(_gitHub.Requests, r => Assert.Equal("/repos/owner/repo", r.Path));
        var log = await SyncLogAsync();
        Assert.Single(log, e => e.Kind == SyncLogEventKind.RepositoryArchived);
        Assert.Contains(log, e => e.Kind == SyncLogEventKind.NoEligibleWorkItems);
    }

    [Fact]
    public async Task Sync_RestoresAnArchivedRepositoryThatIsBackOnGitHub_WithoutMakingItAFavoriteAgain()
    {
        await AcceptAsync(WorkItemType.Epic, "Epic");
        await Repositories.SetFavoriteAsync(Repo, true, _cancellationToken);
        _gitHub.DeletedRepositories.Add(Repo);
        await Sync.SyncAcceptedAsync(_cancellationToken);
        _gitHub.DeletedRepositories.Clear();

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(1, result.Items);
        Assert.Empty(await Repositories.GetArchivedAsync(_cancellationToken));
        var restored = (await Repositories.FindAsync(Repo, _cancellationToken))!;
        Assert.Equal((false, null, false), (restored.IsArchived, restored.ArchivedAt, restored.IsFavorite));
        var log = await SyncLogAsync();
        Assert.Equal(SyncLogLevel.Information, Assert.Single(log, e => e.Kind == SyncLogEventKind.RepositoryRestored).Level);
    }

    [Fact]
    public async Task Sync_IgnoresARepositoryArchivedOnGitHub_WithoutArchivingOrLoggingIt()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        await Repositories.SetFavoriteAsync(Repo, true, _cancellationToken);
        _gitHub.IssuesOnGitHub[42] = new("Renamed", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        _gitHub.GitHubArchivedRepositories.Add(Repo);
        _gitHub.Requests.Clear();

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal((1, 0, 0), (result.Repositories, result.Items, result.Changed));
        Assert.Empty(result.Failures);
        Assert.Equal("Epic", (await _service.GetAsync(Repo, epic.Id, _cancellationToken))!.Title);
        Assert.All(_gitHub.Requests, r => Assert.Equal("/repos/owner/repo", r.Path));
        var stored = (await Repositories.FindAsync(Repo, _cancellationToken))!;
        Assert.Equal((false, true), (stored.IsArchived, stored.IsFavorite));
        Assert.DoesNotContain(await SyncLogAsync(), e => e.RepositoryFullName == Repo);
    }

    [Fact]
    public async Task Sync_WhenARepositoryCannotBeChecked_KeepsItAndStillSyncsItsWorkItems()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new("Renamed", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        _gitHub.RepositoryReadFailure = (HttpStatusCode.Forbidden, "Resource not accessible by personal access token");

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);

        var failure = Assert.Single(result.Failures);
        Assert.Equal((Repo, (int?)null), (failure.RepositoryFullName, failure.IssueNumber));
        Assert.Equal(1, result.Changed);
        Assert.False((await Repositories.FindAsync(Repo, _cancellationToken))!.IsArchived);
        var failed = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.RepositoryFailed);
        Assert.Contains("Could not check whether owner/repo still exists on GitHub", failed.Message);
    }

    [Fact]
    public async Task Sync_WhenGitHubRefusesTheTokenWhileCheckingRepositories_StopsBeforeAnyWorkItem()
    {
        await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.RepositoryReadFailure = (HttpStatusCode.Unauthorized, "Bad credentials");
        _gitHub.Requests.Clear();

        var error = await Assert.ThrowsAsync<GitHubApiException>(() => Sync.SyncAcceptedAsync(_cancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(["/repos/owner/repo"], _gitHub.Requests.Select(r => r.Path));
        var stopped = Assert.Single(await SyncLogAsync());
        Assert.Equal((SyncLogEventKind.RunStopped, SyncLogLevel.Error), (stopped.Kind, stopped.Level));
        Assert.Contains("refused the token", stopped.Message);
        Assert.False((await Repositories.FindAsync(Repo, _cancellationToken))!.IsArchived);
    }

    [Fact]
    public async Task ArchivedRepository_CannotBecomeAFavorite()
    {
        await Repositories.RecordAccessAsync("owner/gone", _cancellationToken);
        Assert.True(await Repositories.SetArchivedAsync("owner/gone", true, _cancellationToken));
        Assert.False(await Repositories.SetArchivedAsync("owner/gone", true, _cancellationToken));
        Assert.False(await Repositories.SetArchivedAsync("owner/unknown", true, _cancellationToken));

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => Repositories.SetFavoriteAsync("owner/gone", true, _cancellationToken));

        Assert.Contains("removed from GitHub", error.Message);
        Assert.Empty(await Repositories.GetFavoritesAsync(_cancellationToken));
        Assert.Null(await Repositories.FindAsync("owner/unknown", _cancellationToken));
    }
}

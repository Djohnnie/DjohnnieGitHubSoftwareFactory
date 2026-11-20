using System.Text.Json;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>Asking the item's agent to fix the merge conflicts of its pull request: by the sync, or with the button.</summary>
public partial class WorkItemServiceTests
{
    private List<string> CommentsPostedOnPullRequest7()
    {
        return _gitHub.Requests
            .Where(r => r is { Method: "POST", Path: "/repos/owner/repo/issues/7/comments" })
            .Select(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("body").GetString()!)
            .ToList();
    }

    [Fact]
    public void NewItems_FixMergeConflictsByDefault()
    {
        Assert.True(new WorkItem().FixMergeConflicts);
    }

    [Fact]
    public async Task Sync_AsksCopilotOncePerConflict_ToFixTheMergeConflicts()
    {
        var task = await TaskWithPullRequestAsync();
        Assert.True(task.FixMergeConflicts);
        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", CopilotBranch, Mergeable: "CONFLICTING")];

        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        var posted = Assert.Single(CommentsPostedOnPullRequest7());
        Assert.Equal($"@copilot {MergeConflictFixes.Request}", posted);
        var stored = (await _service.GetAsync(Repo, task.Id, _cancellationToken))!;
        Assert.NotNull(stored.MergeConflictFixRequestedAt);
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.MergeConflictFixRequested);
        Assert.Equal((SyncLogLevel.Information, task.Id), (logged.Level, logged.WorkItemId));
        Assert.Contains("Copilot was asked to fix them", logged.Message);

        // Once the conflicts are gone the request is forgotten, so a new conflict is asked about again.
        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", CopilotBranch, Mergeable: "MERGEABLE")];
        await Sync.SyncAcceptedAsync(_cancellationToken);
        Assert.Null((await _service.GetAsync(Repo, task.Id, _cancellationToken))!.MergeConflictFixRequestedAt);

        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", CopilotBranch, Mergeable: "CONFLICTING")];
        await Sync.SyncAcceptedAsync(_cancellationToken);
        Assert.Equal(2, CommentsPostedOnPullRequest7().Count);
    }

    [Fact]
    public async Task Sync_DoesntAsk_WhenTheItemDoesntFixMergeConflicts()
    {
        var saved = await _service.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Task", Description = "About Task.", FixMergeConflicts = false
        }, _cancellationToken);
        Assert.False(saved.FixMergeConflicts);
        var task = (await _service.AcceptAsync(Repo, saved.Id, _cancellationToken)).WorkItem;
        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", CopilotBranch, Mergeable: "CONFLICTING")];
        Assert.True((await RefreshAssignedTaskAsync(task)).PullRequestHasConflicts);

        await Sync.SyncAcceptedAsync(_cancellationToken);
        Assert.Empty(CommentsPostedOnPullRequest7());
    }

    [Fact]
    public async Task Sync_DoesntAsk_WhileCopilotIsWorking()
    {
        await TaskWithPullRequestAsync();
        _gitHub.LinkedPullRequests[42] = [new(7, "[WIP] Task", "OPEN", CopilotBranch, Mergeable: "CONFLICTING")];

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(CommentsPostedOnPullRequest7());
    }

    [Fact]
    public async Task Sync_AsksTheWorkerToFixTheMergeConflicts_WithACommentAndARevisionRun()
    {
        var task = await WorkerTaskWithPullRequestAsync();
        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", "dghsf/42-task-1a2b3c4d", Mergeable: "CONFLICTING")];

        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal([MergeConflictFixes.Request], CommentsPostedOnPullRequest7());
        var runs = await _services.GetRequiredService<AgentRunService>().GetRunsAsync(Repo, task.Id, _cancellationToken);
        var revision = Assert.Single(runs, r => r.Kind == AgentRunKind.Revise);
        Assert.Equal((AgentRunStatus.Pending, 7), (revision.Status, revision.PullRequestNumber));
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.MergeConflictFixRequested);
        Assert.Contains("the DGHSF agent worker was asked to fix them", logged.Message);
    }

    [Fact]
    public async Task AskToFix_PostsTheRequest_AndTheSyncDoesntAskAgainForTheSameConflict()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", CopilotBranch, Mergeable: "CONFLICTING")];
        await RefreshAssignedTaskAsync(task);

        var result = await _service.RequestMergeConflictFixAsync(Repo, task.Id, _cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(("https://github.com/owner/repo/pull/7#issuecomment-1", false, (string?)null), (result.Url, result.AgentRunStarted, result.Warning));
        Assert.Equal([$"@copilot {MergeConflictFixes.Request}"], CommentsPostedOnPullRequest7());
        Assert.NotNull((await _service.GetAsync(Repo, task.Id, _cancellationToken))!.MergeConflictFixRequestedAt);
    }
}

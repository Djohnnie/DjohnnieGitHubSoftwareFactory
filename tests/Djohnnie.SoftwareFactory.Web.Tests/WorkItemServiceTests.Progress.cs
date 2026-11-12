using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    private async Task<WorkItem> RefreshAssignedTaskAsync(WorkItem task, string state = "open")
    {
        _gitHub.IssuesOnGitHub[42] = new("Task", $"About Task.\n\n{WorkItemWorkflow.Marker(task)}", state, "Copilot");
        return (await _service.RefreshAsync(Repo, task.Id, _cancellationToken)).WorkItem;
    }

    [Fact]
    public async Task Refresh_WithoutPullRequest_IsQueuedForCopilot()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");

        var refreshed = await RefreshAssignedTaskAsync(task);

        Assert.Equal(CopilotProgress.Queued, refreshed.Progress);
        Assert.Null(refreshed.PullRequestNumber);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path == "/repos/owner/repo/actions/runs");
    }

    [Fact]
    public async Task Refresh_WhileCopilotsRunIsInProgress_IsWorkingWithThePullRequestAndRun()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.LinkedPullRequests[42] = [new(7, "[WIP] Task", "OPEN", "copilot/fix-42", Checks: "PENDING")];
        _gitHub.RunsByBranch["copilot/fix-42"] = [new("in_progress", null)];

        var refreshed = await RefreshAssignedTaskAsync(task);

        Assert.Equal(CopilotProgress.Working, refreshed.Progress);
        Assert.Equal(7, refreshed.PullRequestNumber);
        Assert.Equal("https://github.com/owner/repo/pull/7", refreshed.PullRequestUrl);
        Assert.Equal(PullRequestChecks.Pending, refreshed.PullRequestChecks);
        Assert.Equal("https://github.com/owner/repo/actions/runs/500", refreshed.AgentRunUrl);
        Assert.Equal("copilot/fix-42", refreshed.PullRequestBranch);
        Assert.Equal(WorkItemStatus.InProgress, refreshed.Status);
    }

    [Fact]
    public async Task Refresh_IgnoresRunsOfTheRepositorysOwnWorkflows()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", "copilot/fix-42", Checks: "SUCCESS")];
        _gitHub.RunsByBranch["copilot/fix-42"] = [new("in_progress", null, "pull_request", ".github/workflows/ci.yml"), new("completed", "success")];

        var refreshed = await RefreshAssignedTaskAsync(task);

        Assert.Equal(CopilotProgress.ReadyForReview, refreshed.Progress);
        Assert.Equal("https://github.com/owner/repo/actions/runs/501", refreshed.AgentRunUrl);
        Assert.Equal(PullRequestChecks.Passing, refreshed.PullRequestChecks);
    }

    [Fact]
    public async Task Refresh_WhenCopilotsRunFailed_IsStopped()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.LinkedPullRequests[42] = [new(7, "[WIP] Task", "OPEN", "copilot/fix-42")];
        _gitHub.RunsByBranch["copilot/fix-42"] = [new("completed", "failure")];

        Assert.Equal(CopilotProgress.Stopped, (await RefreshAssignedTaskAsync(task)).Progress);
    }

    [Fact]
    public async Task Refresh_WithoutActionsAccess_FallsBackToTheWorkInProgressTitle()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.RunsFailure = HttpStatusCode.Forbidden;
        _gitHub.LinkedPullRequests[42] = [new(7, "[WIP] Task", "OPEN", "copilot/fix-42")];

        var working = await RefreshAssignedTaskAsync(task);
        Assert.Equal(CopilotProgress.Working, working.Progress);
        Assert.Null(working.AgentRunUrl);

        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", "copilot/fix-42", LatestReviews: ["CHANGES_REQUESTED"])];
        Assert.Equal(CopilotProgress.ChangesRequested, (await RefreshAssignedTaskAsync(task)).Progress);
    }

    [Fact]
    public async Task Refresh_AfterMerging_IsMergedAndDone()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.LinkedPullRequests[42] = [new(6, "Task", "CLOSED", "copilot/first-try"), new(7, "Task", "MERGED", "copilot/fix-42")];

        var refreshed = await RefreshAssignedTaskAsync(task, "closed");

        Assert.Equal(CopilotProgress.Merged, refreshed.Progress);
        Assert.Equal(7, refreshed.PullRequestNumber);
        Assert.Equal(WorkItemStatus.Done, refreshed.Status);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path == "/repos/owner/repo/actions/runs");
    }

    [Fact]
    public async Task Refresh_WhenThePullRequestsCantBeRead_StillRefreshesTheIssueAndKeepsTheProgress()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.LinkedPullRequests[42] = [new(7, "[WIP] Task", "OPEN", "copilot/fix-42")];
        await RefreshAssignedTaskAsync(task);
        _gitHub.GraphQlFailure = (HttpStatusCode.Forbidden, "Resource not accessible by integration");

        var refreshed = await RefreshAssignedTaskAsync(task, "closed");

        Assert.Equal(WorkItemStatus.Done, refreshed.Status);
        Assert.Equal(CopilotProgress.Working, refreshed.Progress);
        Assert.Equal(7, refreshed.PullRequestNumber);
    }

    [Fact]
    public async Task Refresh_WhenTheRateLimitIsReachedForThePullRequests_Fails()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.GraphQlFailure = (HttpStatusCode.Forbidden, "API rate limit exceeded");

        await Assert.ThrowsAsync<Djohnnie.SoftwareFactory.Application.Features.GitHub.GitHubApiException>(() => RefreshAssignedTaskAsync(task));
    }

    [Fact]
    public async Task Refresh_OfAnEpic_DoesNotLookForPullRequests()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new("Epic", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");

        var refreshed = (await _service.RefreshAsync(Repo, epic.Id, _cancellationToken)).WorkItem;

        Assert.Equal(CopilotProgress.None, refreshed.Progress);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path == "/graphql");
    }

    [Fact]
    public async Task Refresh_WhenOnlyTheProgressChanged_IsAChange()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        await RefreshAssignedTaskAsync(task);
        _gitHub.LinkedPullRequests[42] = [new(7, "[WIP] Task", "OPEN", "copilot/fix-42")];

        var result = await _service.RefreshAsync(Repo, task.Id, _cancellationToken);

        Assert.True(result.Changed);
        Assert.Equal(CopilotProgress.Working, result.WorkItem.Progress);
    }
}

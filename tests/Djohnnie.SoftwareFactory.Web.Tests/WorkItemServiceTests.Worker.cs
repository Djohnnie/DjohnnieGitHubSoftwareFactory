using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>Items that run on the DGHSF agent worker: accepting queues a run instead of assigning Copilot, and the run drives the progress.</summary>
public partial class WorkItemServiceTests
{
    private async Task<WorkItem> AcceptWorkerTaskAsync(string? model = "claude-sonnet-4.5")
    {
        var task = await _service.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Task", Description = "About Task.",
            Executor = AgentExecutor.DghsfWorker, Model = model
        }, _cancellationToken);
        Assert.Equal(AgentExecutor.DghsfWorker, task.Executor);
        return (await _service.AcceptAsync(Repo, task.Id, _cancellationToken)).WorkItem;
    }

    private async Task<WorkItem> RefreshWorkerTaskAsync(WorkItem task)
    {
        // The worker never assigns anyone on GitHub.
        _gitHub.IssuesOnGitHub[42] = new("Task", $"About Task.\n\n{Application.Features.WorkItems.WorkItemWorkflow.Marker(task)}", "open");
        return (await _service.RefreshAsync(Repo, task.Id, _cancellationToken)).WorkItem;
    }

    private async Task SetRunAsync(Guid runId, AgentRunStatus status, int? pullRequest = null)
    {
        var factory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(_cancellationToken);
        var run = await db.AgentRuns.SingleAsync(r => r.Id == runId, _cancellationToken);
        run.Status = status;
        run.PullRequestNumber = pullRequest;
        run.PullRequestUrl = pullRequest is null ? null : $"https://github.com/owner/repo/pull/{pullRequest}";
        run.Branch = pullRequest is null ? null : "dghsf/42-task-1a2b3c4d";
        await db.SaveChangesAsync(_cancellationToken);
    }

    [Fact]
    public async Task Accept_AnItemForTheWorker_QueuesARunInsteadOfAssigningCopilot()
    {
        var task = await AcceptWorkerTaskAsync();

        Assert.Equal(WorkItemStatus.InProgress, task.Status);
        Assert.NotNull(task.AssignedAt);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path.EndsWith("/assignees"));
        var run = Assert.Single(await _services.GetRequiredService<AgentRunService>().GetRunsAsync(Repo, task.Id, _cancellationToken));
        Assert.Equal((AgentRunStatus.Pending, "claude-sonnet-4.5", 2, "owner/repo"), (run.Status, run.RequestedModel, run.MaxAttempts, run.RepositoryFullName));
        Assert.Null(run.AiCredits);
    }

    [Fact]
    public async Task Save_KeepsTheExecutorOnlyForTypesThatAreAssigned()
    {
        var epic = await _service.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Epic", Description = "About Epic.", Executor = AgentExecutor.DghsfWorker
        }, _cancellationToken);

        Assert.Equal(AgentExecutor.CopilotCloudAgent, epic.Executor);
    }

    [Fact]
    public async Task Refresh_AnItemForTheWorker_FollowsItsRunAndThenItsPullRequest()
    {
        var task = await AcceptWorkerTaskAsync();
        var run = Assert.Single(await _services.GetRequiredService<AgentRunService>().GetRunsAsync(Repo, task.Id, _cancellationToken));

        var queued = await RefreshWorkerTaskAsync(task);
        Assert.Equal((WorkItemStatus.InProgress, CopilotProgress.Queued), (queued.Status, queued.Progress));

        await SetRunAsync(run.Id, AgentRunStatus.Running);
        Assert.Equal(CopilotProgress.Working, (await RefreshWorkerTaskAsync(task)).Progress);

        // Until GitHub links the pull request to the issue, the run's own pull request is shown.
        await SetRunAsync(run.Id, AgentRunStatus.Succeeded, pullRequest: 7);
        var opened = await RefreshWorkerTaskAsync(task);
        Assert.Equal((CopilotProgress.ReadyForReview, 7, "dghsf/42-task-1a2b3c4d"), (opened.Progress, opened.PullRequestNumber, opened.PullRequestBranch));

        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", "dghsf/42-task-1a2b3c4d", ReviewDecision: "CHANGES_REQUESTED", Checks: "SUCCESS")];
        var reviewed = await RefreshWorkerTaskAsync(task);
        Assert.Equal((CopilotProgress.ChangesRequested, PullRequestChecks.Passing), (reviewed.Progress, reviewed.PullRequestChecks));
        Assert.Null(reviewed.AgentRunUrl);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path == "/repos/owner/repo/actions/runs");
    }

    [Fact]
    public async Task Refresh_AfterTheWorkersRunFailed_IsStopped_AndAnotherRunCanStart()
    {
        var task = await AcceptWorkerTaskAsync();
        var runs = _services.GetRequiredService<AgentRunService>();
        var first = Assert.Single(await runs.GetRunsAsync(Repo, task.Id, _cancellationToken));

        var refused = await Assert.ThrowsAsync<DomainRuleException>(() => runs.StartRunAsync(Repo, task.Id, _cancellationToken));
        Assert.Contains("already working", refused.Message);

        await SetRunAsync(first.Id, AgentRunStatus.Failed);
        Assert.Equal(CopilotProgress.Stopped, (await RefreshWorkerTaskAsync(task)).Progress);

        var second = await runs.StartRunAsync(Repo, task.Id, _cancellationToken);
        Assert.Equal(AgentRunStatus.Pending, second.Status);
        Assert.Equal([second.Id, first.Id], (await runs.GetRunsAsync(Repo, task.Id, _cancellationToken)).Select(r => r.Id));
        Assert.Equal(CopilotProgress.Queued, (await RefreshWorkerTaskAsync(task)).Progress);
    }

    [Fact]
    public async Task Refresh_AnItemWhoseRunsWereAllCancelled_IsAcceptedAgain_AndCanBeAssignedAgain()
    {
        var task = await AcceptWorkerTaskAsync();
        var runs = _services.GetRequiredService<AgentRunService>();
        var run = Assert.Single(await runs.GetRunsAsync(Repo, task.Id, _cancellationToken));
        await SetRunAsync(run.Id, AgentRunStatus.Cancelled);

        var accepted = await RefreshWorkerTaskAsync(task);
        Assert.Equal((WorkItemStatus.Accepted, CopilotProgress.None), (accepted.Status, accepted.Progress));

        var assigned = await _service.AssignAgentAsync(Repo, task.Id, _cancellationToken);
        Assert.Null(assigned.AssignmentError);
        Assert.Equal(WorkItemStatus.InProgress, assigned.WorkItem.Status);
        Assert.Equal(2, (await runs.GetRunsAsync(Repo, task.Id, _cancellationToken)).Count);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path.EndsWith("/assignees"));
    }

    [Fact]
    public async Task StartRun_OfAnItemForCopilot_IsRefused()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");

        await Assert.ThrowsAsync<DomainRuleException>(() => _services.GetRequiredService<AgentRunService>().StartRunAsync(Repo, task.Id, _cancellationToken));
    }
}

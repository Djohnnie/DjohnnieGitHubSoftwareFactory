using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>
/// Feedback for the DGHSF agent worker, and starting another worker run by hand, first refresh the item and its parents,
/// because the worker reads them from the database.
/// </summary>
public partial class WorkItemServiceTests
{
    /// <summary>Epic #42 with a worker task #43 below it, whose first run opened pull request #7.</summary>
    private async Task<(WorkItem Epic, WorkItem Task)> EpicWithWorkerTaskAsync()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        var saved = await _service.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, ParentId = epic.Id, Title = "Task", Description = "About Task.",
            Executor = AgentExecutor.DghsfWorker
        }, _cancellationToken);
        var task = (await _service.AcceptAsync(Repo, saved.Id, _cancellationToken)).WorkItem;
        var runs = _services.GetRequiredService<AgentRunService>();
        await SetRunAsync(Assert.Single(await runs.GetRunsAsync(Repo, task.Id, _cancellationToken)).Id, AgentRunStatus.Succeeded, pullRequest: 7);
        _gitHub.IssuesOnGitHub[42] = new("Epic", $"About Epic.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        _gitHub.IssuesOnGitHub[43] = new("Task", $"About Task.\n\n{WorkItemWorkflow.Marker(task)}", "open");
        task = (await _service.RefreshAsync(Repo, task.Id, _cancellationToken)).WorkItem;
        Assert.Equal(7, task.PullRequestNumber);
        return (epic, task);
    }

    [Fact]
    public async Task FeedbackForTheWorker_FirstRefreshesTheItemAndItsParents_ThenPostsAndStartsTheRevision()
    {
        var (epic, task) = await EpicWithWorkerTaskAsync();
        _gitHub.IssuesOnGitHub[42] = new("Epic renamed", $"The epic as it is now.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        _gitHub.IssuesOnGitHub[43] = new("Task renamed", $"The task as it is now.\n\n{WorkItemWorkflow.Marker(task)}", "open");
        _gitHub.Requests.Clear();

        var result = await _service.SendPullRequestFeedbackAsync(new(Repo, task.Id, "Please add tests.", []), _cancellationToken);

        Assert.True(result.AgentRunStarted);
        Assert.Equal(("Task renamed", "The task as it is now."), await TitleAndDescriptionAsync(task.Id));
        Assert.Equal(("Epic renamed", "The epic as it is now."), await TitleAndDescriptionAsync(epic.Id));
        var posted = _gitHub.Requests.FindIndex(r => r is { Method: "POST", Path: "/repos/owner/repo/issues/7/comments" });
        Assert.True(_gitHub.Requests.FindIndex(r => r is { Method: "GET", Path: "/repos/owner/repo/issues/43" }) is >= 0 and var item && item < posted);
        Assert.True(_gitHub.Requests.FindIndex(r => r is { Method: "GET", Path: "/repos/owner/repo/issues/42" }) is >= 0 and var parent && parent < posted);
    }

    [Fact]
    public async Task FeedbackForTheWorker_WhenAParentCantBeRefreshed_IsNotSent()
    {
        var (_, task) = await EpicWithWorkerTaskAsync();
        _gitHub.IssuesOnGitHub[42] = null;

        var refused = await Assert.ThrowsAsync<DomainRuleException>(() => _service.SendPullRequestFeedbackAsync(new(Repo, task.Id, "Please add tests.", []), _cancellationToken));

        Assert.Contains("The epic 'Epic' couldn't be refreshed from GitHub", refused.Message);
        Assert.Contains("Your feedback wasn't sent", refused.Message);
        await AssertNothingSentAsync(task);
    }

    [Fact]
    public async Task FeedbackForTheWorker_OnAnItemWhoseIssueWasClosed_IsNotSent()
    {
        var (_, task) = await EpicWithWorkerTaskAsync();
        _gitHub.IssuesOnGitHub[43] = new("Task", $"About Task.\n\n{WorkItemWorkflow.Marker(task)}", "closed");

        var refused = await Assert.ThrowsAsync<DomainRuleException>(() => _service.SendPullRequestFeedbackAsync(new(Repo, task.Id, "Please add tests.", []), _cancellationToken));

        Assert.Contains("GitHub issue #43 is closed", refused.Message);
        await AssertNothingSentAsync(task);
    }

    [Fact]
    public async Task FeedbackForCopilot_DoesntRefreshTheItemFirst_BecauseCopilotReadsTheIssueOnGitHub()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.Requests.Clear();

        await _service.SendPullRequestFeedbackAsync(new(Repo, task.Id, "Please add tests.", []), _cancellationToken);

        Assert.DoesNotContain(_gitHub.Requests, r => r is { Method: "GET", Path: "/repos/owner/repo/issues/42" });
    }

    [Fact]
    public async Task RetryingARevision_FirstRefreshesTheItemAndItsParents()
    {
        var (epic, task) = await EpicWithWorkerTaskAsync();
        _gitHub.IssuesOnGitHub[42] = new("Epic renamed", $"The epic as it is now.\n\n{WorkItemWorkflow.Marker(epic)}", "open");
        _gitHub.IssuesOnGitHub[43] = new("Task renamed", $"The task as it is now.\n\n{WorkItemWorkflow.Marker(task)}", "open");

        var revision = await _services.GetRequiredService<AgentRunService>().StartRevisionAsync(Repo, task.Id, _cancellationToken);

        Assert.Equal(AgentRunKind.Revise, revision.Kind);
        Assert.Equal(("Task renamed", "The task as it is now."), await TitleAndDescriptionAsync(task.Id));
        Assert.Equal(("Epic renamed", "The epic as it is now."), await TitleAndDescriptionAsync(epic.Id));
    }

    [Fact]
    public async Task StartingOver_FirstRefreshesTheItemAndItsParents()
    {
        var (epic, task) = await EpicWithWorkerTaskAsync();
        _gitHub.IssuesOnGitHub[42] = new("Epic renamed", $"The epic as it is now.\n\n{WorkItemWorkflow.Marker(epic)}", "open");

        var run = await _services.GetRequiredService<AgentRunService>().StartRunAsync(Repo, task.Id, _cancellationToken);

        Assert.Equal(AgentRunKind.Implement, run.Kind);
        Assert.Equal(("Epic renamed", "The epic as it is now."), await TitleAndDescriptionAsync(epic.Id));
    }

    [Fact]
    public async Task RetryingOrStartingOver_WhenAParentCantBeRefreshed_StartsNothing()
    {
        var (_, task) = await EpicWithWorkerTaskAsync();
        _gitHub.IssuesOnGitHub[42] = null;
        var runs = _services.GetRequiredService<AgentRunService>();

        var revision = await Assert.ThrowsAsync<DomainRuleException>(() => runs.StartRevisionAsync(Repo, task.Id, _cancellationToken));
        var startOver = await Assert.ThrowsAsync<DomainRuleException>(() => runs.StartRunAsync(Repo, task.Id, _cancellationToken));

        Assert.All([revision, startOver], refused => Assert.Contains("The epic 'Epic' couldn't be refreshed from GitHub", refused.Message));
        await AssertNothingSentAsync(task);
    }

    [Fact]
    public async Task RetryingARevision_OnAnItemWhoseIssueWasClosed_StartsNothing()
    {
        var (_, task) = await EpicWithWorkerTaskAsync();
        _gitHub.IssuesOnGitHub[43] = new("Task", $"About Task.\n\n{WorkItemWorkflow.Marker(task)}", "closed");

        var refused = await Assert.ThrowsAsync<DomainRuleException>(() => _services.GetRequiredService<AgentRunService>().StartRevisionAsync(Repo, task.Id, _cancellationToken));

        Assert.Contains("GitHub issue #43 is closed", refused.Message);
        await AssertNothingSentAsync(task);
    }

    private async Task<(string Title, string Description)> TitleAndDescriptionAsync(Guid id)
    {
        var item = (await _service.GetAsync(Repo, id, _cancellationToken))!;
        return (item.Title, item.Description);
    }

    private async Task AssertNothingSentAsync(WorkItem task)
    {
        Assert.DoesNotContain(_gitHub.Requests, r => r is { Method: "POST", Path: "/repos/owner/repo/issues/7/comments" });
        var run = Assert.Single(await _services.GetRequiredService<AgentRunService>().GetRunsAsync(Repo, task.Id, _cancellationToken));
        Assert.Equal(AgentRunKind.Implement, run.Kind);
    }
}

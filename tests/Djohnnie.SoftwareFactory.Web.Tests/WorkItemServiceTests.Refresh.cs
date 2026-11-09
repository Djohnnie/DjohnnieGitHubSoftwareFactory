using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    [Fact]
    public async Task Refresh_StoresTheIssuesTitleAndDescriptionWithoutTheMarker()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new(" Renamed ", $"Edited\r\non GitHub.\r\n\r\n{WorkItemWorkflow.Marker(epic)}\r\n", "open");

        var result = await _service.RefreshAsync(Repo, epic.Id, _cancellationToken);

        Assert.True(result.Changed);
        var stored = await _service.GetAsync(Repo, epic.Id, _cancellationToken);
        Assert.Equal("Renamed", stored!.Title);
        Assert.Equal("Edited\non GitHub.", stored.Description);
        Assert.Equal(WorkItemStatus.Accepted, stored.Status);
        Assert.True(stored.UpdatedAt > epic.UpdatedAt);
    }

    [Fact]
    public async Task Refresh_RemovesMarkersOfEarlierFormats()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new("Epic", "Edited.\n\n<!-- dghsf:epic:17 -->\n", "open");

        await _service.RefreshAsync(Repo, epic.Id, _cancellationToken);

        Assert.Equal("Edited.", (await _service.GetAsync(Repo, epic.Id, _cancellationToken))!.Description);
    }

    [Fact]
    public async Task Refresh_WhenOnlyLineEndingsDiffer_IsNotAChange()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        await SetDescriptionAsync(epic.Id, "Line one\nLine two\n");
        epic = (await _service.GetAsync(Repo, epic.Id, _cancellationToken))!;
        _gitHub.IssuesOnGitHub[42] = new("Epic", $"Line one\r\nLine two\r\n\r\n{WorkItemWorkflow.Marker(epic)}", "open");

        var result = await _service.RefreshAsync(Repo, epic.Id, _cancellationToken);

        Assert.False(result.Changed);
        Assert.Equal("Line one\nLine two\n", result.WorkItem.Description);
        Assert.Equal(epic.UpdatedAt, result.WorkItem.UpdatedAt);
    }

    [Fact]
    public async Task Refresh_FollowsTheIssuesStateAndCopilotAssignment()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        Assert.Equal(WorkItemStatus.InProgress, task.Status);
        var marker = WorkItemWorkflow.Marker(task);

        async Task<WorkItem> RefreshWithAsync(string state, params string[] assignees)
        {
            _gitHub.IssuesOnGitHub[42] = new("Task", $"About Task.\n\n{marker}", state, assignees);
            return (await _service.RefreshAsync(Repo, task.Id, _cancellationToken)).WorkItem;
        }

        var unassigned = await RefreshWithAsync("open", "someone");
        Assert.Equal(WorkItemStatus.Accepted, unassigned.Status);
        Assert.Null(unassigned.AssignedAt);

        var assigned = await RefreshWithAsync("open", "Copilot");
        Assert.Equal(WorkItemStatus.InProgress, assigned.Status);
        Assert.NotNull(assigned.AssignedAt);

        var closed = await RefreshWithAsync("closed", "Copilot");
        Assert.Equal(WorkItemStatus.Done, closed.Status);
        Assert.Equal(assigned.AssignedAt, closed.AssignedAt);

        Assert.Equal(WorkItemStatus.InProgress, (await RefreshWithAsync("open", "Copilot")).Status);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Method != "GET" && r.Path.EndsWith("/issues/42"));
    }

    [Fact]
    public async Task Refresh_NeverPutsAnEpicInProgress()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new("Epic", "About Epic.", "open", "Copilot");

        var result = await _service.RefreshAsync(Repo, epic.Id, _cancellationToken);

        Assert.Equal(WorkItemStatus.Accepted, result.WorkItem.Status);
        Assert.Null(result.WorkItem.AssignedAt);
    }

    [Fact]
    public async Task Refresh_OfADraft_IsRefusedWithoutAskingGitHub()
    {
        var draft = await SaveAsync(WorkItemType.Bug, "Crash");

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.RefreshAsync(Repo, draft.Id, _cancellationToken));

        Assert.Contains("accepted", error.Message);
        Assert.Empty(_gitHub.Requests);
    }

    [Fact]
    public async Task Refresh_WhenTheIssueIsGone_KeepsTheItemAndSaysSo()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = null;

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.RefreshAsync(Repo, epic.Id, _cancellationToken));

        Assert.Contains("#42 no longer exists", error.Message);
        Assert.Equal(epic, await _service.GetAsync(Repo, epic.Id, _cancellationToken));
    }

    [Fact]
    public async Task Refresh_WhenTheDescriptionIsTooLongToStore_ChangesNothing()
    {
        var epic = await AcceptAsync(WorkItemType.Epic, "Epic");
        _gitHub.IssuesOnGitHub[42] = new("Renamed", new string('x', WorkItem.MaxDescriptionLength + 1), "closed");

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.RefreshAsync(Repo, epic.Id, _cancellationToken));

        Assert.Contains("nothing was refreshed", error.Message);
        Assert.Equal(epic, await _service.GetAsync(Repo, epic.Id, _cancellationToken));
    }

    private async Task<WorkItem> AcceptAsync(WorkItemType type, string title)
    {
        var item = await SaveAsync(type, title);
        return (await _service.AcceptAsync(Repo, item.Id, _cancellationToken)).WorkItem;
    }

    private async Task SetDescriptionAsync(Guid id, string description)
    {
        var factory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(_cancellationToken);
        var entity = await db.WorkItems.SingleAsync(e => e.Id == id, _cancellationToken);
        entity.Description = description;
        await db.SaveChangesAsync(_cancellationToken);
    }
}

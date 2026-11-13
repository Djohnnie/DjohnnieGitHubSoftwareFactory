using Djohnnie.SoftwareFactory.Application.Features.Mindmaps;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    private MindmapService Mindmaps => _services.GetRequiredService<MindmapService>();

    [Fact]
    public async Task Refresh_ClosedIssue_MarksTheMindmapIdeasLinkedToTheItemDone()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        var other = await _service.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Other", Description = "Bug." }, _cancellationToken);
        var map = Mindmap.Create(Repo, "Plan");
        var linked = MindmapLayout.AddChild(map, map.Root!.Id, "Do the task");
        var unrelated = MindmapLayout.AddChild(map, map.Root!.Id, "Fix the other bug");
        unrelated.WorkItemId = other.Id;
        var saved = await Mindmaps.SaveAsync(map, _cancellationToken);
        await Mindmaps.LinkAsync(Repo, saved.Id, linked.Id, task.Id, _cancellationToken);

        _gitHub.IssuesOnGitHub[42] = new("Task", $"About Task.\n\n{WorkItemWorkflow.Marker(task)}", "open", "Copilot");
        await _service.RefreshAsync(Repo, task.Id, _cancellationToken);
        Assert.False((await Mindmaps.GetAsync(Repo, saved.Id, _cancellationToken))!.Find(linked.Id)!.IsDone);

        _gitHub.IssuesOnGitHub[42] = new("Task", $"About Task.\n\n{WorkItemWorkflow.Marker(task)}", "closed", "Copilot");
        await _service.RefreshAsync(Repo, task.Id, _cancellationToken);

        var stored = await Mindmaps.GetAsync(Repo, saved.Id, _cancellationToken);
        Assert.True(stored!.Find(linked.Id)!.IsDone);
        Assert.Equal(task.Id, stored.Find(linked.Id)!.WorkItemId);
        Assert.False(stored.Find(unrelated.Id)!.IsDone);

        // An idea linked (or saved) later to an item that is closed already is done right away.
        var later = stored.Copy();
        var again = MindmapLayout.AddChild(later, later.Root!.Id, "Same task again");
        again.WorkItemId = task.Id;
        Assert.True((await Mindmaps.SaveAsync(later, _cancellationToken)).Find(again.Id)!.IsDone);
        var relinked = await Mindmaps.LinkAsync(Repo, saved.Id, unrelated.Id, task.Id, _cancellationToken);
        Assert.True(relinked.Find(unrelated.Id)!.IsDone);
    }

    [Fact]
    public async Task LinkMindmapIdea_RefusesUnknownItemsAndIdeas_AndUnlinks()
    {
        var bug = await _service.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Bug", Description = "Bug." }, _cancellationToken);
        var map = Mindmap.Create(Repo, "Plan");
        var idea = MindmapLayout.AddChild(map, map.Root!.Id, "Fix it");
        var saved = await Mindmaps.SaveAsync(map, _cancellationToken);

        await Assert.ThrowsAsync<DomainRuleException>(() => Mindmaps.LinkAsync(Repo, saved.Id, idea.Id, Guid.NewGuid(), _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => Mindmaps.LinkAsync(Repo, saved.Id, Guid.NewGuid(), bug.Id, _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => Mindmaps.LinkAsync("owner/other", saved.Id, idea.Id, null, _cancellationToken));

        var linked = await Mindmaps.LinkAsync(Repo, saved.Id, idea.Id, bug.Id, _cancellationToken);
        Assert.Equal(bug.Id, linked.Find(idea.Id)!.WorkItemId);
        Assert.False(linked.Find(idea.Id)!.IsDone);
        Assert.Equal(saved.Find(idea.Id)!.X, linked.Find(idea.Id)!.X);

        var unlinked = await Mindmaps.LinkAsync(Repo, saved.Id, idea.Id, null, _cancellationToken);
        Assert.Null(unlinked.Find(idea.Id)!.WorkItemId);
    }

    [Fact]
    public async Task GenerateMindmapWorkItem_SendsTheWholeMapTheTemplateAndTheParent_AndStoresNothing()
    {
        var chat = new ScriptedChatClient(ScriptedChatClient.Text("""{"title":" Passkey login ","description":"## Goal\nSign in without a password."}"""));
        await using var services = await ServicesWithChatAsync(chat);
        var mindmaps = services.GetRequiredService<MindmapService>();
        var items = services.GetRequiredService<WorkItemService>();
        var templates = services.GetRequiredService<Application.Features.WorkItemTemplates.WorkItemTemplateService>();
        var template = await templates.SaveAsync(new Contracts.WorkItemTemplates.WorkItemTemplate
        {
            Type = WorkItemType.Feature, Name = "Standard", TitleTemplate = "Add [capability]", Description = "## Goal skeleton"
        }, _cancellationToken);
        var epic = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Security", Description = "Safer sign-in." }, _cancellationToken);
        var map = Mindmap.Create(Repo, "Release 2.0");
        var security = MindmapLayout.AddChild(map, map.Root!.Id, "Security");
        security.WorkItemId = epic.Id;
        var passkeys = MindmapLayout.AddChild(map, security.Id, "Passkeys");
        MindmapLayout.AddChild(map, passkeys.Id, "Works on phones");
        MindmapLayout.AddChild(map, map.Root!.Id, "Marketing").IsDone = true;
        var saved = await mindmaps.SaveAsync(map, _cancellationToken);

        var written = await mindmaps.GenerateWorkItemAsync(new(Repo, saved.Id, passkeys.Id, WorkItemType.Feature, template.Id, epic.Id), _cancellationToken);

        Assert.Equal(new GeneratedWorkItem("Passkey login", "## Goal\nSign in without a password."), written);
        var input = string.Join("\n", Assert.Single(chat.Requests).Where(m => m.Role == Microsoft.Extensions.AI.ChatRole.User).Select(m => m.Text));
        Assert.Contains("- Release 2.0\n  - Security [linked to epic: Security]\n    - Passkeys  ⟵ write the work item for this idea\n      - Works on phones\n  - Marketing [done]", input.ReplaceLineEndings("\n"));
        Assert.Contains("## Goal skeleton", input);
        Assert.Contains("Safer sign-in.", input);
        Assert.Contains("You are the Mindmap Work Item Writer", InternalAgentTests.SystemPrompt(chat, 0));
        Assert.Single(await items.GetAllAsync(Repo, cancellationToken: _cancellationToken));
    }

    [Fact]
    public async Task GenerateMindmapWorkItem_WithoutAConfiguredAgent_ExplainsWhy()
    {
        var map = Mindmap.Create(Repo, "Plan");
        var idea = MindmapLayout.AddChild(map, map.Root!.Id, "Passkeys");
        var saved = await Mindmaps.SaveAsync(map, _cancellationToken);

        var error = await Assert.ThrowsAsync<DomainRuleException>(() =>
            Mindmaps.GenerateWorkItemAsync(new(Repo, saved.Id, idea.Id, WorkItemType.Epic, null, null), _cancellationToken));

        Assert.Contains("Mindmap Work Item Writer", error.Message);
    }
}

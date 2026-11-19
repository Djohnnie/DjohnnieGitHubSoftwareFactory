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

    private const string PlanResponse = """
        {"items":[
          {"key":3,"parent":2,"type":"User story","title":"Passkeys","description":"## Story\nSign in with a passkey.","ideas":[3,4,4,1]},
          {"key":1,"parent":null,"type":"Epic","title":" Release 2.0 ","description":"## Goal\nShip it.","ideas":[1]},
          {"key":2,"parent":1,"type":"Feature","title":"Login","description":"Sign in.","ideas":[2]},
          {"key":4,"parent":1,"type":"Feature","title":"Search","description":"A feature without user stories.","ideas":[5]},
          {"key":5,"parent":3,"type":"Task","title":"A task","description":"Not part of a plan.","ideas":[]},
          {"key":6,"parent":1,"type":"User story","title":"Below the epic","description":"A story can't be part of an epic.","ideas":[]},
          {"key":7,"parent":42,"type":"User story","title":"Unknown parent","description":"","ideas":[]},
          {"key":8,"parent":1,"type":"Feature","title":"Website","description":"A site.","ideas":[]},
          {"key":9,"parent":8,"type":"User story","title":"Landing page","description":"A page.","ideas":[6,99]},
          {"key":3,"parent":1,"type":"Feature","title":"Duplicate key","description":"","ideas":[]}
        ]}
        """;

    /// <summary>Release 2.0 with Login > Passkeys > Works on phones, Search, Legacy (linked to a bug) and Shipped (done).</summary>
    private async Task<(Mindmap Map, WorkItem Bug)> SavePlanningMapAsync(IServiceProvider services)
    {
        var bug = await services.GetRequiredService<WorkItemService>().SaveAsync(
            new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Old bug", Description = "Bug." }, _cancellationToken);
        var map = Mindmap.Create(Repo, "Release 2.0");
        var login = MindmapLayout.AddChild(map, map.Root!.Id, "Login");
        var passkeys = MindmapLayout.AddChild(map, login.Id, "Passkeys");
        MindmapLayout.AddChild(map, passkeys.Id, "Works on phones");
        MindmapLayout.AddChild(map, map.Root!.Id, "Search");
        MindmapLayout.AddChild(map, map.Root!.Id, "Legacy").WorkItemId = bug.Id;
        MindmapLayout.AddChild(map, map.Root!.Id, "Shipped").IsDone = true;
        return (await services.GetRequiredService<MindmapService>().SaveAsync(map, _cancellationToken), bug);
    }

    private static Guid IdeaId(Mindmap map, string text)
    {
        return map.Nodes.Single(n => n.Text == text).Id;
    }

    private static string UserInput(ScriptedChatClient chat)
    {
        return string.Join("\n", Assert.Single(chat.Requests).Where(m => m.Role == Microsoft.Extensions.AI.ChatRole.User).Select(m => m.Text)).ReplaceLineEndings("\n");
    }

    [Fact]
    public async Task GenerateMindmapPlan_SendsTheNumberedMap_AndKeepsAnEpicWithFeaturesThatEndInUserStories_ParentsFirst()
    {
        var chat = new ScriptedChatClient(ScriptedChatClient.Text(PlanResponse));
        await using var services = await ServicesWithChatAsync(chat);
        var mindmaps = services.GetRequiredService<MindmapService>();
        var templates = services.GetRequiredService<Application.Features.WorkItemTemplates.WorkItemTemplateService>();
        await templates.SaveAsync(new Contracts.WorkItemTemplates.WorkItemTemplate
        {
            Type = WorkItemType.Story, Name = "Standard", TitleTemplate = "As a [user]", Description = "## Story skeleton"
        }, _cancellationToken);
        var (map, _) = await SavePlanningMapAsync(services);

        var plan = await mindmaps.GeneratePlanAsync(Repo, map.Id, _cancellationToken);

        Assert.Null(plan.EpicId);
        Assert.Equal(
        [
            (WorkItemType.Epic, "Release 2.0"), (WorkItemType.Feature, "Login"), (WorkItemType.Feature, "Website"),
            (WorkItemType.Story, "Landing page"), (WorkItemType.Story, "Passkeys")
        ], plan.Items.Select(i => (i.Type, i.Title)));
        var byTitle = plan.Items.ToDictionary(i => i.Title);
        Assert.Null(byTitle["Release 2.0"].ParentId);
        Assert.Equal(byTitle["Release 2.0"].Id, byTitle["Login"].ParentId);
        Assert.Equal(byTitle["Website"].Id, byTitle["Landing page"].ParentId);
        Assert.Equal(byTitle["Login"].Id, byTitle["Passkeys"].ParentId);
        Assert.All(plan.Items, i => Assert.Null(i.ParentWorkItemId));
        Assert.Equal([map.Root!.Id], byTitle["Release 2.0"].IdeaIds);
        Assert.Equal([IdeaId(map, "Passkeys"), IdeaId(map, "Works on phones")], byTitle["Passkeys"].IdeaIds);
        Assert.Empty(byTitle["Landing page"].IdeaIds);

        var input = UserInput(chat);
        Assert.Contains("- [1] Release 2.0\n  - [2] Login\n    - [3] Passkeys\n      - [4] Works on phones\n  - [5] Search\n  - [6] Legacy [linked to bug: Old bug]\n  - [7] Shipped [done]", input);
        Assert.Contains("## Story skeleton", input);
        Assert.Contains("You are the Mindmap Planner", InternalAgentTests.SystemPrompt(chat, 0));
        Assert.Single(await services.GetRequiredService<WorkItemService>().GetAllAsync(Repo, cancellationToken: _cancellationToken));
    }

    [Fact]
    public async Task GenerateMindmapPlan_WithAnEpicWithoutUserStories_IsRefused()
    {
        var chat = new ScriptedChatClient(ScriptedChatClient.Text("""
            {"items":[
              {"key":1,"parent":null,"type":"Epic","title":"Release","description":"","ideas":[1]},
              {"key":2,"parent":1,"type":"Feature","title":"Login","description":"","ideas":[2]},
              {"key":3,"parent":2,"type":"Task","title":"Do it","description":"","ideas":[3]}
            ]}
            """));
        await using var services = await ServicesWithChatAsync(chat);
        var (map, _) = await SavePlanningMapAsync(services);

        await Assert.ThrowsAsync<DomainRuleException>(() => services.GetRequiredService<MindmapService>().GeneratePlanAsync(Repo, map.Id, _cancellationToken));
    }

    [Fact]
    public async Task SaveMindmapPlan_CreatesTheDraftsUnderTheirParents_AndLinksThemToTheirIdeas()
    {
        var chat = new ScriptedChatClient(ScriptedChatClient.Text(PlanResponse));
        await using var services = await ServicesWithChatAsync(chat);
        var mindmaps = services.GetRequiredService<MindmapService>();
        var items = services.GetRequiredService<WorkItemService>();
        var (map, bug) = await SavePlanningMapAsync(services);
        var plan = await mindmaps.GeneratePlanAsync(Repo, map.Id, _cancellationToken);
        var byTitle = plan.Items.ToDictionary(i => i.Title);

        // Nothing is stored without the epic, or with a feature without user stories.
        var withoutEpic = await Assert.ThrowsAsync<DomainRuleException>(() => mindmaps.SavePlanAsync(Repo, map.Id, plan.Items.Skip(1).ToList(), _cancellationToken));
        Assert.Contains("epic", withoutEpic.Message);
        var withoutStory = await Assert.ThrowsAsync<DomainRuleException>(() => mindmaps.SavePlanAsync(Repo, map.Id,
            plan.Items.Where(i => i.Title != "Passkeys").ToList(), _cancellationToken));
        Assert.Contains("user stories", withoutStory.Message);
        Assert.Single(await items.GetAllAsync(Repo, cancellationToken: _cancellationToken));

        var saved = (await mindmaps.SavePlanAsync(Repo, map.Id, plan.Items, _cancellationToken)).ToDictionary(i => i.Title);

        Assert.Equal(5, saved.Count);
        Assert.All(saved.Values, item => Assert.Equal(WorkItemStatus.Draft, item.Status));
        Assert.Null(saved["Release 2.0"].ParentId);
        Assert.Equal(saved["Release 2.0"].Id, saved["Login"].ParentId);
        Assert.Equal(saved["Release 2.0"].Id, saved["Website"].ParentId);
        Assert.Equal(saved["Login"].Id, saved["Passkeys"].ParentId);
        Assert.Equal(saved["Website"].Id, saved["Landing page"].ParentId);
        Assert.Equal("## Story\nSign in with a passkey.", saved["Passkeys"].Description);
        Assert.DoesNotContain(saved.Values, item => item.Id == byTitle["Passkeys"].Id);

        var stored = (await mindmaps.GetAsync(Repo, map.Id, _cancellationToken))!;
        Assert.Equal(saved["Release 2.0"].Id, stored.Find(IdeaId(map, "Release 2.0"))!.WorkItemId);
        Assert.Equal(saved["Login"].Id, stored.Find(IdeaId(map, "Login"))!.WorkItemId);
        Assert.Equal(saved["Passkeys"].Id, stored.Find(IdeaId(map, "Passkeys"))!.WorkItemId);
        Assert.Equal(saved["Passkeys"].Id, stored.Find(IdeaId(map, "Works on phones"))!.WorkItemId);
        Assert.Null(stored.Find(IdeaId(map, "Search"))!.WorkItemId);
        Assert.Equal(bug.Id, stored.Find(IdeaId(map, "Legacy"))!.WorkItemId);
        Assert.Equal(2, (await mindmaps.GetLinkedIdeasAsync(Repo, saved["Passkeys"].Id, _cancellationToken)).Count);

        // The ideas are linked now, so the same plan can't be saved twice.
        await Assert.ThrowsAsync<DomainRuleException>(() => mindmaps.SavePlanAsync(Repo, map.Id, plan.Items, _cancellationToken));
        Assert.Equal(6, (await items.GetAllAsync(Repo, cancellationToken: _cancellationToken)).Count);
    }

    [Fact]
    public async Task GenerateMindmapPlan_AddsToTheEpicAndFeaturesThatExist_AndRefusesACentralIdeaOfAnotherType()
    {
        var chat = new ScriptedChatClient(ScriptedChatClient.Text("""
            {"items":[
              {"key":1,"parent":null,"type":"Epic","title":"Release again","description":"","ideas":[1]},
              {"key":2,"parent":null,"existingParentIdea":1,"type":"Feature","title":"Login","description":"","ideas":[2]},
              {"key":3,"parent":2,"type":"User story","title":"Passkeys","description":"","ideas":[3,4]},
              {"key":4,"parent":null,"existingParentIdea":6,"type":"User story","title":"Below the bug","description":"","ideas":[]},
              {"key":5,"parent":null,"existingParentIdea":5,"type":"User story","title":"Better search","description":"","ideas":[]}
            ]}
            """));
        await using var services = await ServicesWithChatAsync(chat);
        var mindmaps = services.GetRequiredService<MindmapService>();
        var items = services.GetRequiredService<WorkItemService>();
        var epic = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Release", Description = "Ship." }, _cancellationToken);
        var feature = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Feature, ParentId = epic.Id, Title = "Existing search", Description = "F." }, _cancellationToken);
        var (map, _) = await SavePlanningMapAsync(services);
        await mindmaps.LinkAsync(Repo, map.Id, map.Root!.Id, epic.Id, _cancellationToken);
        await mindmaps.LinkAsync(Repo, map.Id, IdeaId(map, "Search"), feature.Id, _cancellationToken);

        var plan = await mindmaps.GeneratePlanAsync(Repo, map.Id, _cancellationToken);

        Assert.Equal(epic.Id, plan.EpicId);
        Assert.Equal(["Login", "Passkeys", "Better search"], plan.Items.Select(i => i.Title));
        Assert.Contains("Feature: Existing search", UserInput(chat));
        var saved = (await mindmaps.SavePlanAsync(Repo, map.Id, plan.Items, _cancellationToken)).ToDictionary(i => i.Title);
        Assert.Equal(epic.Id, saved["Login"].ParentId);
        Assert.Equal(feature.Id, saved["Better search"].ParentId);

        await mindmaps.LinkAsync(Repo, map.Id, map.Root!.Id, saved["Login"].Id, _cancellationToken);
        var error = await Assert.ThrowsAsync<DomainRuleException>(() => mindmaps.GeneratePlanAsync(Repo, map.Id, _cancellationToken));
        Assert.Contains("central idea", error.Message);
    }

    [Fact]
    public async Task GenerateMindmapPlan_WithoutAConfiguredOrEnabledAgent_ExplainsWhy()
    {
        var saved = await Mindmaps.SaveAsync(Mindmap.Create(Repo, "Plan"), _cancellationToken);
        var error = await Assert.ThrowsAsync<DomainRuleException>(() => Mindmaps.GeneratePlanAsync(Repo, saved.Id, _cancellationToken));
        Assert.Contains("Mindmap Planner", error.Message);

        var chat = new ScriptedChatClient(ScriptedChatClient.Text(PlanResponse));
        await using var services = await ServicesWithChatAsync(chat);
        await InternalAgentTests.DisableAsync(services, Contracts.InternalAgents.InternalAgentKeys.MindmapPlanner);
        var mindmaps = services.GetRequiredService<MindmapService>();
        var other = await mindmaps.SaveAsync(Mindmap.Create(Repo, "Other"), _cancellationToken);
        await Assert.ThrowsAsync<DomainRuleException>(() => mindmaps.GeneratePlanAsync(Repo, other.Id, _cancellationToken));
        Assert.Empty(chat.Requests);
    }
}

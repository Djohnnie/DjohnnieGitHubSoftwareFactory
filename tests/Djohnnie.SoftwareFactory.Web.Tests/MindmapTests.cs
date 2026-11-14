using System.Net;
using System.Security.Claims;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class MindmapTests : BunitContext
{
    private const string Repo = "owner/repo";
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;

    public MindmapTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddDataProtection();
        Services.AddLogging();
        AddAuthorization().SetAuthorized("Djohnnie").SetClaims(new Claim(GitHubClaims.AccessToken, "token"));
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<RepositoryContext>();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton(TimeProvider.System);
        Services.AddApplication();
        Services.AddSingleton(_ => new AssistantAgentFactory(null, NullLoggerFactory.Instance));
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(new NotFound()) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    private MindmapService Mindmaps => Services.GetRequiredService<MindmapService>();
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void Layout_IdeasAroundTheCentre_FillTheRightAndLeftFirst_WithoutOverlapping()
    {
        var map = Mindmap.Create(Repo);
        var root = map.Root!;

        var ideas = Enumerable.Range(1, 10).Select(i => MindmapLayout.AddChild(map, root.Id, $"Idea {i}")).ToList();

        Assert.True(ideas[0].X > 0 && ideas[0].Y == 0);
        Assert.True(ideas[1].X < 0 && ideas[1].Y == 0);
        Assert.All(ideas, idea => Assert.Equal(root.Id, idea.ParentId));
        AssertNoOverlap(map);
    }

    [Fact]
    public void Layout_IdeasBelowAnIdea_GoOnAwayFromTheCentre_WithoutOverlapping()
    {
        var map = Mindmap.Create(Repo);
        var right = MindmapLayout.AddChild(map, map.Root!.Id, "Right");
        var left = MindmapLayout.AddChild(map, map.Root!.Id, "Left");

        var below = Enumerable.Range(1, 4).Select(i => MindmapLayout.AddChild(map, right.Id, $"Detail {i}")).ToList();
        var leftDetail = MindmapLayout.AddChild(map, left.Id, "Left detail");

        Assert.All(below, idea => Assert.True(idea.X > right.X));
        Assert.True(leftDetail.X < left.X);
        AssertNoOverlap(map);
    }

    [Fact]
    public void Layout_Move_MovesTheIdeaWithEverythingBelowIt()
    {
        var map = Mindmap.Create(Repo);
        var branch = MindmapLayout.AddChild(map, map.Root!.Id, "Branch");
        var leaf = MindmapLayout.AddChild(map, branch.Id, "Leaf");
        var other = MindmapLayout.AddChild(map, map.Root!.Id, "Other");
        var (leafX, leafY, otherX) = (leaf.X, leaf.Y, other.X);

        MindmapLayout.Move(map, branch.Id, 40, -25);

        Assert.Equal((leafX + 40, leafY - 25), (leaf.X, leaf.Y));
        Assert.Equal(otherX, other.X);
        Assert.Equal((0d, 0d), (map.Root!.X, map.Root!.Y));
    }

    [Fact]
    public void Layout_Reparent_MovesTheIdeaBelowAnotherOne_AndRefusesCyclesAndTheCentralIdea()
    {
        var map = Mindmap.Create(Repo);
        var root = map.Root!;
        var first = MindmapLayout.AddChild(map, root.Id, "First");
        var second = MindmapLayout.AddChild(map, root.Id, "Second");
        var leaf = MindmapLayout.AddChild(map, first.Id, "Leaf");

        Assert.False(MindmapLayout.Reparent(map, first.Id, leaf.Id));
        Assert.False(MindmapLayout.Reparent(map, first.Id, first.Id));
        Assert.False(MindmapLayout.Reparent(map, root.Id, second.Id));
        Assert.False(MindmapLayout.CanReparent(map, first.Id, root.Id));

        Assert.True(MindmapLayout.Reparent(map, first.Id, second.Id));

        // Second is on the left, so the moved branch now grows further to the left.
        Assert.Equal(second.Id, first.ParentId);
        Assert.Equal(first.Id, leaf.ParentId);
        Assert.True(first.X < second.X && leaf.X < first.X);
        Assert.Null(map.StructureProblem());
        AssertNoOverlap(map);
    }

    [Fact]
    public void Layout_Remove_RemovesTheIdeaWithEverythingBelowIt_ButNeverTheCentralIdea()
    {
        var map = Mindmap.Create(Repo);
        var branch = MindmapLayout.AddChild(map, map.Root!.Id, "Branch");
        MindmapLayout.AddChild(map, branch.Id, "Leaf");
        var other = MindmapLayout.AddChild(map, map.Root!.Id, "Other");

        Assert.Equal(0, MindmapLayout.Remove(map, map.Root!.Id));
        Assert.Equal(2, MindmapLayout.Remove(map, branch.Id));

        Assert.Equal(new[] { map.Root!.Id, other.Id }, map.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Layout_BranchesHaveTheirOwnColour_AndLinesCurveFromParentToChild()
    {
        var map = Mindmap.Create(Repo);
        var first = MindmapLayout.AddChild(map, map.Root!.Id, "First");
        var second = MindmapLayout.AddChild(map, map.Root!.Id, "Second");
        var leaf = MindmapLayout.AddChild(map, second.Id, "Leaf");

        Assert.Equal(-1, MindmapLayout.BranchOf(map, map.Root!.Id));
        Assert.Equal(0, MindmapLayout.BranchOf(map, first.Id));
        Assert.Equal(1, MindmapLayout.BranchOf(map, leaf.Id));
        Assert.Equal("M0 0 C100 0 100 50 200 50",
            MindmapLayout.EdgePath(new MindmapNode { X = 0, Y = 0 }, new MindmapNode { X = 200, Y = 50 }));
        Assert.Equal("M0 0 C0 -100.5 10 -100.5 10 -201",
            MindmapLayout.EdgePath(new MindmapNode { X = 0, Y = 0 }, new MindmapNode { X = 10, Y = -201 }));
    }

    [Fact]
    public async Task Save_StoresTheIdeasPerRepository_AndReplacesThemOnTheNextSave()
    {
        var map = Mindmap.Create(Repo, "  Release 2.0  ");
        var docs = MindmapLayout.AddChild(map, map.Root!.Id, "Write the docs ");
        MindmapLayout.AddChild(map, map.Root!.Id, "Fix the bugs").IsDone = true;

        var saved = await Mindmaps.SaveAsync(map, _cancellationToken);

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal("Release 2.0", saved.Title);
        Assert.Equal("Write the docs", saved.Find(docs.Id)!.Text);
        Assert.Equal((docs.X, docs.Y), (saved.Find(docs.Id)!.X, saved.Find(docs.Id)!.Y));
        var summary = Assert.Single(await Mindmaps.GetAllAsync(Repo, _cancellationToken));
        Assert.Equal((saved.Id, "Release 2.0", 3, 1), (summary.Id, summary.Title, summary.IdeaCount, summary.DoneCount));
        Assert.Empty(await Mindmaps.GetAllAsync("owner/other", _cancellationToken));
        Assert.Null(await Mindmaps.GetAsync("owner/other", saved.Id, _cancellationToken));

        var edited = saved.Copy();
        MindmapLayout.Remove(edited, docs.Id);
        edited.Root!.Text = "Release 2.1";
        var updated = await Mindmaps.SaveAsync(edited, _cancellationToken);

        Assert.Equal(saved.Id, updated.Id);
        var loaded = await Mindmaps.GetAsync(Repo, saved.Id, _cancellationToken);
        Assert.Equal(2, loaded!.Nodes.Count);
        Assert.Equal("Release 2.1", loaded.Title);
        Assert.Equal(3, saved.Nodes.Count);

        await Mindmaps.DeleteAsync(Repo, saved.Id, _cancellationToken);
        Assert.Empty(await Mindmaps.GetAllAsync(Repo, _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => Mindmaps.SaveAsync(edited, _cancellationToken));
    }

    [Fact]
    public async Task Save_RefusesMapsThatAreNotOneTreeOfIdeas()
    {
        var map = Mindmap.Create(Repo);
        var idea = MindmapLayout.AddChild(map, map.Root!.Id, "Idea");

        var empty = map.Copy();
        empty.Find(idea.Id)!.Text = "  ";
        var twoRoots = map.Copy();
        twoRoots.Find(idea.Id)!.ParentId = null;
        var unknownParent = map.Copy();
        unknownParent.Find(idea.Id)!.ParentId = Guid.NewGuid();
        var cycle = map.Copy();
        cycle.Nodes.Add(new MindmapNode { Id = Guid.NewGuid(), ParentId = idea.Id, Text = "A" });
        cycle.Find(idea.Id)!.ParentId = cycle.Nodes[^1].Id;
        var tooLong = map.Copy();
        tooLong.Find(idea.Id)!.Text = new string('x', Mindmap.MaxTextLength + 1);

        foreach (var invalid in new[] { empty, twoRoots, unknownParent, cycle, tooLong, new Mindmap { RepositoryFullName = Repo } })
        {
            await Assert.ThrowsAsync<ValidationFailedException>(() => Mindmaps.SaveAsync(invalid, _cancellationToken));
        }
        Assert.Empty(await Mindmaps.GetAllAsync(Repo, _cancellationToken));
    }

    [Fact]
    public async Task Page_StartsWithACentralIdea_AddsMovesAndSavesIdeas()
    {
        await SelectRepositoryAsync();
        Navigation.NavigateTo(MindmapRoutes.NewPath);

        var cut = Render<Mindmaps>();

        // A new map starts by naming its central idea, in place.
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".mindmap-node.mindmap-root")));
        Assert.Contains("mindmap-selected", cut.Find(".mindmap-root").ClassList);
        Assert.Equal(Mindmap.DefaultIdea, cut.Find(".mindmap-root input.mindmap-edit").GetAttribute("value"));
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Contains("Save mindmap")).HasAttribute("disabled"));

        await cut.InvokeAsync(() => cut.Find(".mindmap-root input.mindmap-edit").Input("  Release 2.0 "));
        await cut.InvokeAsync(() => cut.Find(".mindmap-root input.mindmap-edit").KeyDown("Enter"));

        Assert.Empty(cut.FindAll("input.mindmap-edit"));
        Assert.Equal("Release 2.0", cut.Find(".mindmap-root .mindmap-label").TextContent);
        Assert.False(cut.FindAll("button").Single(b => b.TextContent.Contains("Save mindmap")).HasAttribute("disabled"));

        await cut.InvokeAsync(() => cut.Find(".mindmap-root button[aria-label='Add idea below']").Click());

        var nodes = cut.FindAll(".mindmap-node");
        Assert.Equal(2, nodes.Count);
        Assert.Single(cut.FindAll("path[data-to]"));
        var idea = nodes.Single(n => !n.ClassList.Contains("mindmap-root"));
        Assert.Contains("mindmap-selected", idea.ClassList);
        Assert.Contains("branch-0", idea.ClassList);
        var id = idea.GetAttribute("data-node")!;

        // The new idea is edited in place right away; Escape keeps its text, Tab adds another idea below it.
        await cut.InvokeAsync(() => cut.Find($"[data-node='{id}'] input.mindmap-edit").Input("Changed my mind"));
        await cut.InvokeAsync(() => cut.Find($"[data-node='{id}'] input.mindmap-edit").KeyDown("Escape"));
        Assert.Equal("New idea", cut.Find($"[data-node='{id}'] .mindmap-label").TextContent);
        await cut.InvokeAsync(() => cut.Instance.EditFromBrowser(id));
        await cut.InvokeAsync(() => cut.Find($"[data-node='{id}'] input.mindmap-edit").Input("Docs"));
        await cut.InvokeAsync(() => cut.Find($"[data-node='{id}'] input.mindmap-edit").KeyDown("Tab"));
        Assert.Equal("Docs", cut.Find($"[data-node='{id}'] .mindmap-label").TextContent);
        Assert.Equal(id, cut.Find("input.mindmap-edit").ParentElement!.GetAttribute("data-parent"));
        await cut.InvokeAsync(() => cut.Find("input.mindmap-edit").KeyDown("Enter"));
        await cut.InvokeAsync(() => cut.Find(".mindmap-selected button[aria-label='Remove idea']").Click());
        Assert.Equal(2, cut.FindAll(".mindmap-node").Count);
        var x = double.Parse(idea.GetAttribute("data-x")!, System.Globalization.CultureInfo.InvariantCulture);

        await cut.InvokeAsync(() => cut.Instance.Move(id, 30, 10, null));
        cut.WaitForAssertion(() => Assert.Equal(
            (x + 30).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            cut.Find($"[data-node='{id}']").GetAttribute("data-x")));
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "settle");

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save mindmap")).Click());

        cut.WaitForAssertion(() => Assert.DoesNotContain(MindmapRoutes.NewPath, Navigation.Uri));
        var saved = Assert.Single(await Mindmaps.GetAllAsync(Repo, _cancellationToken));
        Assert.Equal(("Release 2.0", 2), (saved.Title, saved.IdeaCount));
        Assert.EndsWith(MindmapRoutes.MapPath(saved.Id), Navigation.Uri);
    }

    [Fact]
    public async Task Overview_ShowsACardPerMindmapWithItsProgress_AndANewCard()
    {
        await SelectRepositoryAsync();
        var launch = Mindmap.Create(Repo, "Launch");
        MindmapLayout.AddChild(launch, launch.Root!.Id, "Marketing").IsDone = true;
        var savedLaunch = await Mindmaps.SaveAsync(launch, _cancellationToken);
        var savedRelease = await Mindmaps.SaveAsync(Mindmap.Create(Repo, "Release"), _cancellationToken);
        Navigation.NavigateTo(MindmapRoutes.ListPath);

        var cut = Render<MindmapOverview>();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("a.mindmap-card").Count));
        var card = cut.FindAll("a.mindmap-card").Single(c => c.TextContent.Contains("Launch"));
        Assert.EndsWith(MindmapRoutes.MapPath(savedLaunch.Id), card.GetAttribute("href"));
        Assert.Contains("1 of 2 done", card.TextContent);
        Assert.Contains(cut.FindAll("a.mindmap-card"), c => c.GetAttribute("href")!.EndsWith(MindmapRoutes.MapPath(savedRelease.Id)));
        Assert.EndsWith(MindmapRoutes.NewPath, cut.Find("a.mindmap-card-new").GetAttribute("href"));
    }

    [Fact]
    public async Task Overview_WithTheOldMapAddress_OpensTheMap()
    {
        await SelectRepositoryAsync();
        var mapId = Guid.NewGuid();
        var ideaId = Guid.NewGuid();
        Navigation.NavigateTo($"/mindmaps?id={mapId}&idea={ideaId}");

        Render<MindmapOverview>();

        Assert.EndsWith(MindmapRoutes.MapPath(mapId, ideaId), Navigation.Uri);
    }

    [Fact]
    public async Task Page_QuickSwitcher_FiltersTheMindmapsAndOpensThePickedOne()
    {
        await SelectRepositoryAsync();
        var launch = await Mindmaps.SaveAsync(Mindmap.Create(Repo, "Launch"), _cancellationToken);
        var release = await Mindmaps.SaveAsync(Mindmap.Create(Repo, "Release"), _cancellationToken);
        var website = await Mindmaps.SaveAsync(Mindmap.Create(Repo, "Website"), _cancellationToken);
        Navigation.NavigateTo(MindmapRoutes.MapPath(launch.Id));
        var dialogs = Render<MudBlazor.MudDialogProvider>();

        var cut = Render<Mindmaps>(parameters => parameters.Add(page => page.Id, launch.Id));

        cut.WaitForAssertion(() => Assert.Equal("Launch", cut.Find(".mindmap-title").TextContent.Trim()));
        Assert.Empty(cut.FindAll(".mud-select"));
        await cut.InvokeAsync(() => cut.Find(".mindmap-title").Click());
        dialogs.WaitForAssertion(() => Assert.Equal(4, dialogs.FindAll(".switcher-item").Count));
        Assert.Equal("New mindmap", dialogs.FindAll(".switcher-item")[^1].TextContent.Trim());

        await dialogs.InvokeAsync(() => dialogs.Find("input").Input("site"));
        var matches = dialogs.FindAll(".switcher-item");
        Assert.Equal(2, matches.Count);
        Assert.Contains("Website", matches[0].TextContent);
        Assert.DoesNotContain(release.Title, matches[0].TextContent);
        await dialogs.InvokeAsync(() => dialogs.Find("input").KeyDown("Enter"));

        cut.WaitForAssertion(() => Assert.EndsWith(MindmapRoutes.MapPath(website.Id), Navigation.Uri));
    }

    [Fact]
    public async Task Page_DroppingAnIdeaOnAnotherMovesItThere()
    {
        await SelectRepositoryAsync();
        var map = Mindmap.Create(Repo, "Launch");
        var first = MindmapLayout.AddChild(map, map.Root!.Id, "Marketing");
        var second = MindmapLayout.AddChild(map, map.Root!.Id, "Website");
        var saved = await Mindmaps.SaveAsync(map, _cancellationToken);
        Navigation.NavigateTo(MindmapRoutes.MapPath(saved.Id));

        var cut = Render<Mindmaps>(parameters => parameters.Add(page => page.Id, saved.Id));

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".mindmap-node").Count));

        await cut.InvokeAsync(() => cut.Instance.Move(second.Id.ToString(), 5, 5, first.Id.ToString()));

        cut.WaitForAssertion(() => Assert.Equal(first.Id.ToString(), cut.Find($"[data-node='{second.Id}']").GetAttribute("data-parent")));
        Assert.Contains("branch-0", cut.Find($"[data-node='{second.Id}']").ClassList);
        Assert.False(cut.FindAll("button").Single(b => b.TextContent.Contains("Save mindmap")).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Page_ShowsTheLinkedWorkItems_AndTheMenuUnlinksOrCreatesFromAnIdea()
    {
        await SelectRepositoryAsync();
        var items = Services.GetRequiredService<Application.Features.WorkItems.WorkItemService>();
        var epic = await items.SaveAsync(new Contracts.WorkItems.WorkItem
        {
            RepositoryFullName = Repo, Type = Contracts.WorkItems.WorkItemType.Epic, Title = "Security", Description = "Safer."
        }, _cancellationToken);
        var map = Mindmap.Create(Repo, "Release");
        var security = MindmapLayout.AddChild(map, map.Root!.Id, "Security");
        security.WorkItemId = epic.Id;
        var passkeys = MindmapLayout.AddChild(map, security.Id, "Passkeys");
        var gone = MindmapLayout.AddChild(map, map.Root!.Id, "Deleted item");
        gone.WorkItemId = Guid.NewGuid();
        var saved = await Mindmaps.SaveAsync(map, _cancellationToken);
        Navigation.NavigateTo(MindmapRoutes.MapPath(saved.Id));
        var popovers = Render<MudBlazor.MudPopoverProvider>();

        var cut = Render<Mindmaps>(parameters => parameters.Add(page => page.Id, saved.Id));

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".mindmap-node").Count));
        var badge = cut.Find($"[data-node='{security.Id}'] a.mindmap-link");
        Assert.EndsWith($"/epics/{epic.Id}", badge.GetAttribute("href"));
        Assert.Contains("Draft", badge.TextContent);
        Assert.NotNull(cut.Find($"[data-node='{gone.Id}'] .mindmap-link-missing"));

        await cut.InvokeAsync(() => cut.Instance.Select(security.Id.ToString()));
        await cut.InvokeAsync(() => cut.Find(".mindmap-selected button[aria-label='Work item']").Click());
        var menu = popovers.FindAll(".mud-menu-item").Select(item => item.TextContent.Trim()).ToList();
        Assert.Contains(menu, text => text.StartsWith("Open epic"));
        Assert.Contains("Unlink", menu);
        Assert.DoesNotContain("New feature", menu);
        await cut.InvokeAsync(() => popovers.FindAll(".mud-menu-item").Single(item => item.TextContent.Trim() == "Unlink").Click());

        Assert.Empty(cut.FindAll($"[data-node='{security.Id}'] .mindmap-link"));
        Assert.False(cut.FindAll("button").Single(b => b.TextContent.Contains("Save mindmap")).HasAttribute("disabled"));

        // Creating from an idea saves the map first, then opens the new item with the idea and the proposed parent.
        await cut.InvokeAsync(() => cut.Instance.Select(passkeys.Id.ToString()));
        await cut.InvokeAsync(() => cut.Find(".mindmap-selected button[aria-label='Work item']").Click());
        await cut.InvokeAsync(() => popovers.FindAll(".mud-menu-item").Single(item => item.TextContent.Trim() == "New feature").Click());

        cut.WaitForAssertion(() => Assert.EndsWith($"/features/new?mindmap={saved.Id}&idea={passkeys.Id}", Navigation.Uri));
        Assert.Null((await Mindmaps.GetAsync(Repo, saved.Id, _cancellationToken))!.Find(security.Id)!.WorkItemId);
    }

    [Fact]
    public async Task Page_WithAnIdeaInTheUrl_SelectsItAndBringsItIntoView()
    {
        await SelectRepositoryAsync();
        var map = Mindmap.Create(Repo, "Release");
        var idea = MindmapLayout.AddChild(map, map.Root!.Id, "Far away");
        MindmapLayout.Move(map, idea.Id, 600, 300);
        var saved = await Mindmaps.SaveAsync(map, _cancellationToken);
        Navigation.NavigateTo(MindmapRoutes.MapPath(saved.Id, idea.Id));

        var cut = Render<Mindmaps>(parameters => parameters.Add(page => page.Id, saved.Id));

        cut.WaitForAssertion(() => Assert.Contains("mindmap-selected", cut.Find($"[data-node='{idea.Id}']").ClassList));
        var centered = Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "centerOn");
        Assert.Equal(new object?[] { idea.X, idea.Y }, centered.Arguments);
    }

    [Fact]
    public void Layout_ProposedParent_IsTheNearestLinkedIdeaAboveThatCanContainTheType()
    {
        var map = Mindmap.Create(Repo);
        var epicIdea = MindmapLayout.AddChild(map, map.Root!.Id, "Epic idea");
        var featureIdea = MindmapLayout.AddChild(map, epicIdea.Id, "Feature idea");
        var plain = MindmapLayout.AddChild(map, featureIdea.Id, "Plain idea");
        var leaf = MindmapLayout.AddChild(map, plain.Id, "Leaf");
        var epic = new Contracts.WorkItems.WorkItem { Id = Guid.NewGuid(), Type = Contracts.WorkItems.WorkItemType.Epic, Title = "Epic" };
        var feature = new Contracts.WorkItems.WorkItem { Id = Guid.NewGuid(), Type = Contracts.WorkItems.WorkItemType.Feature, Title = "Feature" };
        epicIdea.WorkItemId = epic.Id;
        featureIdea.WorkItemId = feature.Id;
        var items = new Dictionary<Guid, Contracts.WorkItems.WorkItem> { [epic.Id] = epic, [feature.Id] = feature };

        Assert.Equal(feature.Id, MindmapLayout.ProposedParent(map, leaf.Id, Contracts.WorkItems.WorkItemType.Story, items));
        Assert.Equal(feature.Id, MindmapLayout.ProposedParent(map, leaf.Id, Contracts.WorkItems.WorkItemType.Task, items));
        Assert.Equal(epic.Id, MindmapLayout.ProposedParent(map, leaf.Id, Contracts.WorkItems.WorkItemType.Feature, items));
        Assert.Null(MindmapLayout.ProposedParent(map, leaf.Id, Contracts.WorkItems.WorkItemType.Epic, items));
        Assert.Null(MindmapLayout.ProposedParent(map, epicIdea.Id, Contracts.WorkItems.WorkItemType.Story, items));
    }

    private static void AssertNoOverlap(Mindmap map)
    {
        foreach (var a in map.Nodes)
        {
            foreach (var b in map.Nodes.Where(b => b.Id != a.Id))
            {
                Assert.True(
                    Math.Abs(a.X - b.X) >= MindmapLayout.NodeWidth - 0.5 || Math.Abs(a.Y - b.Y) >= MindmapLayout.NodeHeight - 0.5,
                    $"'{a.Text}' ({a.X}, {a.Y}) overlaps '{b.Text}' ({b.X}, {b.Y}).");
            }
        }
    }

    private Task SelectRepositoryAsync()
    {
        return Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", Repo, new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null));
    }

    private sealed class NotFound : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

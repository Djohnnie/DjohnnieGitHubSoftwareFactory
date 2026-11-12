using System.Net;
using System.Security.Claims;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
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

public class WorkItemMapTests : BunitContext
{
    private const string Repo = "owner/repo";
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;

    public WorkItemMapTests()
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

    private WorkItemService Items => Services.GetRequiredService<WorkItemService>();
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void Layout_ExpandedEpic_IsInTheMiddle_WithEachLevelOnItsOwnRing_AndLinesToEveryChild()
    {
        var epic = Item(WorkItemType.Epic, "Passkeys");
        var feature = Item(WorkItemType.Feature, "Passkey login", epic);
        var story = Item(WorkItemType.Story, "Register a passkey", feature);
        var task = Item(WorkItemType.Task, "Add the WebAuthn library", story);
        var bug = Item(WorkItemType.Bug, "Login button misaligned", epic);
        var otherEpic = Item(WorkItemType.Epic, "Billing");
        var otherFeature = Item(WorkItemType.Feature, "Invoices", otherEpic);
        var loose = Item(WorkItemType.Task, "Update dependencies");

        var layout = WorkItemMapLayout.Create([epic, feature, story, task, bug, otherEpic, otherFeature, loose], epic.Id);

        Assert.Equal(epic, layout.Center);
        Assert.Equal(["Billing", "Passkeys"], layout.Epics.Select(e => e.Title));
        Assert.Equal([loose], layout.Loose);
        var center = Assert.Single(layout.Nodes, n => n.Role == WorkItemMapRole.Center);
        Assert.Equal((50d, 50d), (center.X, center.Y));
        Assert.Equal(4, center.DescendantCount);
        Assert.Equal([feature.Id, story.Id, task.Id, bug.Id], layout.Nodes.Where(n => n.Role == WorkItemMapRole.Child).Select(n => n.Item.Id));
        Assert.DoesNotContain(layout.Nodes, n => n.Item.Id == otherFeature.Id || n.Item.Id == loose.Id || n.Item.Id == otherEpic.Id);
        Assert.Equal([1, 2, 3, 1], layout.Nodes.Skip(1).Select(n => n.Depth));
        Assert.True(Distance(layout, feature) < Distance(layout, story));
        Assert.True(Distance(layout, story) < Distance(layout, task));
        Assert.Equal(Distance(layout, feature), Distance(layout, bug), 3);
        Assert.All(layout.Nodes, n => Assert.InRange(n.X, 0, 100));
        Assert.All(layout.Nodes, n => Assert.InRange(n.Y, 0, 100));
        var storyNode = layout.Nodes.Single(n => n.Item.Id == story.Id);
        var featureNode = layout.Nodes.Single(n => n.Item.Id == feature.Id);
        Assert.Equal((featureNode.X, featureNode.Y), (storyNode.FromX, storyNode.FromY));
        Assert.Equal(4, layout.Edges.Count);
        Assert.Contains(layout.Edges, e => e.Key == $"{epic.Id}-{feature.Id}" && e.Path.StartsWith("M50 50 C", StringComparison.Ordinal));
        Assert.Equal([(WorkItemType.Feature, 1), (WorkItemType.Story, 1), (WorkItemType.Bug, 1), (WorkItemType.Task, 1)], layout.DescendantsByType(epic.Id));
    }

    [Fact]
    public void Layout_WithoutAnExpandedEpic_PutsTheEpicsAroundTheHub_AndTheStandaloneItemsFurtherOut()
    {
        var epic = Item(WorkItemType.Epic, "Passkeys");
        var feature = Item(WorkItemType.Feature, "Passkey login", epic);
        var otherEpic = Item(WorkItemType.Epic, "Billing");
        var loose = Item(WorkItemType.Bug, "Crash on start");

        var layout = WorkItemMapLayout.Create([epic, feature, otherEpic, loose], null);

        Assert.Null(layout.Center);
        Assert.Equal([otherEpic.Id, epic.Id, loose.Id], layout.Nodes.Select(n => n.Item.Id));
        Assert.Equal([WorkItemMapRole.Epic, WorkItemMapRole.Epic, WorkItemMapRole.Loose], layout.Nodes.Select(n => n.Role));
        Assert.Equal(1, layout.Nodes.Single(n => n.Item.Id == epic.Id).DescendantCount);
        Assert.True(Distance(layout, epic) < Distance(layout, loose));
        Assert.Equal(2, layout.Edges.Count);
    }

    [Fact]
    public void DefaultEpic_IsTheFirstOpenEpicAlphabetically()
    {
        var closed = Item(WorkItemType.Epic, "Accounts", status: WorkItemStatus.Done);
        var open = Item(WorkItemType.Epic, "Billing");
        var later = Item(WorkItemType.Epic, "Checkout");

        Assert.Equal(open.Id, WorkItemMapLayout.DefaultEpic([later, closed, open]));
        Assert.Equal(closed.Id, WorkItemMapLayout.DefaultEpic([closed]));
        Assert.Null(WorkItemMapLayout.DefaultEpic([Item(WorkItemType.Bug, "Crash")]));
    }

    [Fact]
    public void Layout_SpreadsAFewItemsWideAtFullSize_AndFitsManyCloserTogether()
    {
        var epic = Item(WorkItemType.Epic, "Big");
        var features = Enumerable.Range(1, 120).Select(i => Item(WorkItemType.Feature, $"Feature {i}", epic)).ToList();

        var roomy = Children(WorkItemMapLayout.Create([epic, .. features.Take(5)], epic.Id));
        var full = Children(WorkItemMapLayout.Create([epic, .. features.Take(24)], epic.Id));
        var overfull = Children(WorkItemMapLayout.Create([epic, .. features], epic.Id));

        Assert.All(roomy, n => Assert.Equal(1, n.Scale));
        Assert.All(roomy, n => Assert.False(n.Crowded));
        Assert.Single(roomy.Select(n => Distance(n)).Distinct());
        Assert.True(Distance(roomy[0]) >= 0.8, $"A single ring spreads out, but is at {Distance(roomy[0])}.");

        Assert.All(full, n => Assert.False(n.Crowded));
        Assert.True(full[0].Scale < 1 || full.Select(n => Distance(n)).Distinct().Count() == 2, "A full ring staggers or shrinks its nodes.");

        Assert.All(overfull, n => Assert.True(n.Crowded));
        Assert.All(overfull, n => Assert.True(n.Scale < 0.7));
    }

    [Fact]
    public void Layout_ASmallerMap_FitsTheSameItemsCloserTogether()
    {
        var epic = Item(WorkItemType.Epic, "Big");
        List<WorkItem> items = [epic, .. Enumerable.Range(1, 16).Select(i => Item(WorkItemType.Feature, $"Feature {i}", epic))];

        var wide = Children(WorkItemMapLayout.Create(items, epic.Id, 1600, 800));
        var narrow = Children(WorkItemMapLayout.Create(items, epic.Id, 700, 450));

        Assert.All(wide, n => Assert.Equal(1, n.Scale));
        Assert.True(narrow.Max(n => n.Scale) < 1 || narrow.All(n => n.Crowded));
    }


    [Fact]
    public void Layout_AChainOfSingleChildren_RunsSidewaysAtFullSize()
    {
        var epic = Item(WorkItemType.Epic, "Passkeys");
        var feature = Item(WorkItemType.Feature, "Passkey login", epic);
        var story = Item(WorkItemType.Story, "Register a passkey", feature);
        var task = Item(WorkItemType.Task, "Add the WebAuthn library", story);

        var nodes = Children(WorkItemMapLayout.Create([epic, feature, story, task], epic.Id, 1150, 560));

        Assert.Equal([feature.Id, story.Id, task.Id], nodes.Select(n => n.Item.Id));
        Assert.All(nodes, n => Assert.Equal(50, n.Y, 1));
        Assert.True(nodes[0].X < 50 && nodes[1].X < nodes[0].X && nodes[2].X < nodes[1].X, "The chain runs to the left.");
        Assert.All(nodes, n => Assert.Equal(1, n.Scale));
        Assert.All(nodes, n => Assert.False(n.Crowded));
    }

    [Fact]
    public void Layout_ABranchingTree_KeepsMostLabels_AndOnlyMovesOutTheNodesThatNeedIt()
    {
        var epic = Item(WorkItemType.Epic, "Passkeys");
        List<WorkItem> items = [epic];
        for (var f = 0; f < 3; f++)
        {
            var feature = Item(WorkItemType.Feature, $"Feature {f}", epic);
            items.Add(feature);
            for (var s = 0; s < 3; s++)
            {
                var story = Item(WorkItemType.Story, $"Story {f}{s}", feature);
                items.Add(story);
                items.AddRange(Enumerable.Range(0, 2).Select(t => Item(WorkItemType.Task, $"Task {f}{s}{t}", story)));
            }
        }

        var layout = WorkItemMapLayout.Create(items, epic.Id, 1150, 560);

        var features = layout.Nodes.Where(n => n.Depth == 1).ToList();
        Assert.All(features, n => Assert.True(n.Scale > 0.9));
        Assert.True(features.Select(n => Math.Round(Distance(n), 2)).Distinct().Count() > 1, "Each feature finds its own radius.");
        Assert.True(layout.Nodes.Count(n => n.Crowded) <= 3, $"{layout.Nodes.Count(n => n.Crowded)} labels are hidden.");
        Assert.All(layout.Nodes, n => Assert.InRange(n.Scale, WorkItemMapLayout.MinScale, 1));
    }

    [Fact]
    public void Progress_CountsEverythingBelowTheEpic_WithInProgressAsAccepted()
    {
        var epic = Item(WorkItemType.Epic, "Passkeys", status: WorkItemStatus.Accepted);
        var feature = Item(WorkItemType.Feature, "Passkey login", epic, WorkItemStatus.Accepted);
        var story = Item(WorkItemType.Story, "Register a passkey", feature, WorkItemStatus.InProgress);
        var task = Item(WorkItemType.Task, "Add the WebAuthn library", story, WorkItemStatus.Done);
        var bug = Item(WorkItemType.Bug, "Login button misaligned", epic);
        var other = Item(WorkItemType.Epic, "Billing");
        var otherFeature = Item(WorkItemType.Feature, "Invoices", other, WorkItemStatus.Done);

        var layout = WorkItemMapLayout.Create([epic, feature, story, task, bug, other, otherFeature], epic.Id);

        var progress = layout.Progress(epic.Id);
        Assert.Equal(new WorkItemMapProgress(1, 2, 1), progress);
        Assert.Equal(4, progress.Total);
        Assert.Equal(25, progress.PercentDone);
        Assert.Equal(50, progress.Share(progress.Accepted));
        Assert.Equal(0, layout.Progress(Item(WorkItemType.Epic, "Empty").Id).PercentDone);
        Assert.Equal(99, new WorkItemMapProgress(1, 0, 199).PercentDone);
    }

    [Fact]
    public async Task Page_ShowsTheProgressOfTheExpandedEpicOnly()
    {
        await SelectRepositoryAsync();
        var billing = await SaveAsync(WorkItemType.Epic, "Billing");
        await SaveAsync(WorkItemType.Feature, "Invoices", billing.Id);
        await SaveAsync(WorkItemType.Feature, "Refunds", billing.Id);
        await SaveAsync(WorkItemType.Epic, "Passkeys");
        Navigation.NavigateTo("/work-item-map");

        var cut = Render<WorkItemMap>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".map-progress-strip")));
        var bar = cut.Find(".map-progress-bar");
        Assert.Equal("0", bar.GetAttribute("aria-valuenow"));
        Assert.Equal("0% completed: 0 closed, 0 accepted, 2 draft", bar.GetAttribute("aria-valuetext"));
        Assert.Equal("width:100%", cut.Find(".map-progress-part.status-draft").GetAttribute("style"));
        Assert.Contains("2 draft", cut.Find(".map-progress-counts").TextContent);

        await cut.InvokeAsync(() => cut.FindAll(".map-dock button").Single(b => b.TextContent.Contains("Passkeys")).Click());

        Assert.Contains("Nothing below this epic yet", cut.Find(".map-progress-bar").TextContent);

        await cut.InvokeAsync(() => cut.Find(".map-dock-hub").Click());

        Assert.Empty(cut.FindAll(".map-progress-strip"));
    }

    private static List<WorkItemMapNode> Children(WorkItemMapLayout layout)
    {
        return [.. layout.Nodes.Where(n => n.Role == WorkItemMapRole.Child)];
    }

    [Fact]
    public async Task Page_ExpandsTheFirstOpenEpic_LinksEveryItem_AndExpandingAnotherEpicClosesIt()
    {
        await SelectRepositoryAsync();
        var billing = await SaveAsync(WorkItemType.Epic, "Billing");
        var invoices = await SaveAsync(WorkItemType.Feature, "Invoices", billing.Id);
        var passkeys = await SaveAsync(WorkItemType.Epic, "Passkeys");
        var login = await SaveAsync(WorkItemType.Feature, "Passkey login", passkeys.Id);
        var story = await SaveAsync(WorkItemType.Story, "Register a passkey", login.Id);
        Navigation.NavigateTo("/work-item-map");

        var cut = Render<WorkItemMap>();

        cut.WaitForAssertion(() => Assert.Contains("Invoices", cut.Find(".map-canvas").TextContent));
        Assert.Contains("Billing", cut.Find(".map-node-center").TextContent);
        Assert.Equal($"/epics/{billing.Id}", cut.Find(".map-node-center a").GetAttribute("href"));
        Assert.Equal($"/features/{invoices.Id}", cut.Find(".map-node-depth1 a").GetAttribute("href"));
        Assert.DoesNotContain("Passkey login", cut.Find(".map-canvas").TextContent);
        Assert.Equal(["Allepics", "Billing1", "Passkeys2"], cut.FindAll(".map-dock button").Select(b => string.Concat(b.TextContent.Where(c => !char.IsWhiteSpace(c)))));

        await cut.InvokeAsync(() => cut.FindAll(".map-dock button").Single(b => b.TextContent.Contains("Passkeys")).Click());

        Assert.Contains("Passkeys", cut.Find(".map-node-center").TextContent);
        Assert.Equal($"/stories/{story.Id}", cut.Find(".map-node-depth2 a").GetAttribute("href"));
        Assert.DoesNotContain("Invoices", cut.Find(".map-canvas").TextContent);
        Assert.Single(cut.FindAll(".map-node-center"));
        Assert.EndsWith($"/work-item-map?epic={passkeys.Id}", Navigation.Uri, StringComparison.Ordinal);
        Assert.Equal("true", cut.FindAll(".map-dock button").Single(b => b.TextContent.Contains("Passkeys")).GetAttribute("aria-pressed"));

        await cut.InvokeAsync(() => cut.Find(".map-node-center .map-toggle").Click());

        Assert.Empty(cut.FindAll(".map-node-center"));
        Assert.NotNull(cut.Find(".map-hub"));
        Assert.Equal(2, cut.FindAll(".map-node-epic").Count);
        Assert.EndsWith("/work-item-map?epic=all", Navigation.Uri, StringComparison.Ordinal);

        await cut.InvokeAsync(() => cut.FindAll(".map-node-epic .map-toggle").Single(b => b.GetAttribute("aria-label") == "Expand Billing").Click());

        Assert.Contains("Billing", cut.Find(".map-node-center").TextContent);
        Assert.Empty(cut.FindAll(".map-hub"));
    }

    [Fact]
    public async Task Page_TheEpicInTheUrl_IsExpandedFromTheStart()
    {
        await SelectRepositoryAsync();
        await SaveAsync(WorkItemType.Epic, "Billing");
        var passkeys = await SaveAsync(WorkItemType.Epic, "Passkeys");
        Navigation.NavigateTo($"/work-item-map?epic={passkeys.Id}");

        var cut = Render<WorkItemMap>();

        cut.WaitForAssertion(() => Assert.Contains("Passkeys", cut.Find(".map-node-center").TextContent));
        Assert.Contains("Nothing below this epic yet", cut.Markup);
    }

    [Fact]
    public async Task Page_WithoutWorkItems_OffersANewEpic()
    {
        await SelectRepositoryAsync();
        Navigation.NavigateTo("/work-item-map");

        var cut = Render<WorkItemMap>();

        cut.WaitForAssertion(() => Assert.Contains("This repository has no work items yet.", cut.Markup));
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/epics/new");
        Assert.Empty(cut.FindAll(".map-dock"));
    }

    private static double Distance(WorkItemMapLayout layout, WorkItem item)
    {
        return Distance(layout.Nodes.Single(n => n.Item.Id == item.Id));
    }

    private static double Distance(WorkItemMapNode node)
    {
        // The rings are ellipses; compare them as circles. Rounded, because the positions are stored rounded to 2 decimals.
        return Math.Round(Math.Sqrt(Math.Pow((node.X - 50) / 44, 2) + Math.Pow((node.Y - 50) / 41, 2)), 6);
    }

    private static WorkItem Item(WorkItemType type, string title, WorkItem? parent = null, WorkItemStatus status = WorkItemStatus.Draft)
    {
        return new WorkItem { Id = Guid.NewGuid(), RepositoryFullName = Repo, Type = type, Title = title, ParentId = parent?.Id, Status = status };
    }

    private Task<WorkItem> SaveAsync(WorkItemType type, string title, Guid? parentId = null)
    {
        return Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = type, ParentId = parentId, Title = title, Description = "Body" }, _cancellationToken);
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

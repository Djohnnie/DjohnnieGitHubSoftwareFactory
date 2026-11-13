using System.Net;
using System.Security.Claims;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Layout;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class AgentTemplatePagesTests : BunitContext, IAsyncLifetime
{
    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public AgentTemplatePagesTests()
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
        Services.AddSingleton(new AssistantAgentFactory(null, NullLoggerFactory.Instance));
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(new NotFoundHandler()) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    [Fact]
    public async Task Save_NewTemplate_StoresItWithoutARepository()
    {
        Render<MudPopoverProvider>();
        var cut = Render<AgentTemplateEditor>();
        cut.WaitForElement("textarea");

        cut.FindAll("input")[0].Input("Docs Writer");
        cut.WaitForAssertion(() => Assert.Equal("docs-writer", cut.FindAll("input")[1].GetAttribute("value")), TimeSpan.FromSeconds(2));
        cut.FindAll("textarea")[0].Change("Keeps the docs up to date.");
        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("Write clear docs."));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save template")).Click());

        cut.WaitForAssertion(() => Assert.EndsWith("/agent-templates", Services.GetRequiredService<NavigationManager>().Uri), TimeSpan.FromSeconds(2));
        var template = Assert.Single(await Services.GetRequiredService<AgentTemplateService>().GetAllAsync(Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("docs-writer", template.FileName);
        Assert.Equal("Docs Writer", template.Name);
        Assert.Equal("---\nname: Docs Writer\ndescription: Keeps the docs up to date.\n---\n\nWrite clear docs.\n", template.Content);
    }

    [Theory]
    [InlineData(typeof(AgentTemplates), true)]
    [InlineData(typeof(WorkItemTemplates), true)]
    [InlineData(typeof(WorkItemTemplateEditor), true)]
    [InlineData(typeof(Components.Pages.InternalAgents), true)]
    [InlineData(typeof(InternalAgentEditor), true)]
    [InlineData(typeof(SkillEditor), true)]
    [InlineData(typeof(Components.Pages.Skills), false)]
    [InlineData(typeof(WorkItems), false)]
    [InlineData(typeof(WorkItemEditor), false)]
    [InlineData(typeof(Components.Pages.Agents), false)]
    public void Layout_WithoutRepository_OnlyRendersRepositoryIndependentPages(Type pageType, bool rendersPage)
    {
        var cut = Render<MainLayout>(p => p
            .AddCascadingValue(new RouteData(pageType, new Dictionary<string, object?>()))
            .Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        cut.WaitForAssertion(() => Assert.DoesNotContain("mud-progress-linear", cut.Markup));
        Assert.Equal(rendersPage, cut.FindAll("#page").Count == 1);
        Assert.Equal(!rendersPage, cut.Markup.Contains("Select a repository"));
        Assert.Empty(cut.FindAll(".mud-appbar nav"));
        if (rendersPage)
        {
            // Without a repository only the repository-independent pages are linked; the template pages are linked from their list pages.
            var links = cut.FindAll(".app-header > .mud-appbar + .desktop-only > nav a");
            Assert.Equal(new[] { "/internal-agents", "/internal-models", "/sync-log" }, links.Select(link => link.GetAttribute("href")));
            // Like every link in the bar, it's an icon with a tooltip: the label is for screen readers, not a caption.
            var internalAgents = cut.Find(".app-header > .mud-appbar + .desktop-only > nav a[href='/internal-agents']");
            Assert.Equal("Internal agents", internalAgents.GetAttribute("aria-label"));
            Assert.Empty(internalAgents.TextContent.Trim());
            Assert.Contains("Select repository", cut.Find(".mud-appbar").TextContent);
        }
        else
        {
            Assert.Empty(cut.FindAll("nav"));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Navigation_HasNoTemplateLinks(bool hasRepository, bool vertical)
    {
        var cut = Render<AppNavigation>(p => p.Add(n => n.HasRepository, hasRepository).Add(n => n.Vertical, vertical));

        Assert.DoesNotContain(cut.FindAll("a"), a => a.GetAttribute("href")!.Contains("templates", StringComparison.Ordinal));
    }

    [Fact]
    public void Layout_UserName_LinksToGitHubProfile()
    {
        var cut = Render<MainLayout>(p => p
            .AddCascadingValue(new RouteData(typeof(AgentTemplates), new Dictionary<string, object?>())));

        var link = cut.Find(".logout a.current-user-name");
        Assert.Equal("Djohnnie", link.TextContent);
        Assert.Equal("https://github.com/Djohnnie", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener", link.GetAttribute("rel"));
        Assert.Equal("submit", cut.Find(".logout button").GetAttribute("type"));
    }

    [Fact]
    public async Task Layout_WithRepository_ShowsNavigationBelowHeaderAndKeepsRepositorySwitcher()
    {
        var repository = Services.GetRequiredService<RepositoryContext>();
        await repository.LoadAsync();
        await repository.SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null),
            "https://github.com/owner/repo", null, false, false, false, null, "main", null));
        var cut = Render<MainLayout>(p => p
            .AddCascadingValue(new RouteData(typeof(Components.Pages.Agents), new Dictionary<string, object?>())));

        var header = cut.Find(".mud-appbar");
        Assert.Equal("repo", header.QuerySelector(".current-repo-name")!.TextContent);
        Assert.Contains("owner /", header.TextContent);
        Assert.Empty(header.QuerySelectorAll("nav"));
        var navigation = cut.Find(".app-header > .mud-appbar + .desktop-only > nav[aria-label='Main navigation']");
        Assert.Equal(new[]
            {
                "/", "/work-item-map", "/actions", "/copilot-instructions", "/skills", "/agents", "/epics", "/features", "/stories", "/bugs", "/tasks",
                "/copilot-code-review", "/models", "/internal-agents", "/internal-models", "/sync-log"
            },
            navigation.QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Empty(navigation.QuerySelectorAll("[role='group']"));
        Assert.All(cut.FindComponents<MudButton>(), button =>
        {
            Assert.Equal(Variant.Text, button.Instance.Variant);
            Assert.Contains("header-link", button.Instance.Class);
        });
        Assert.Equal("submit", header.QuerySelector(".logout button")!.GetAttribute("type"));

        await cut.InvokeAsync(() => header.QuerySelectorAll("button")
            .Single(button => button.TextContent.Contains("Switch")).Click());

        cut.WaitForAssertion(() =>
            Assert.Equal("owner/repo", cut.FindComponent<RepositoryPicker>().Instance.SelectedFullName));
        Assert.Empty(cut.FindAll(".mud-dialog"));
        Assert.True(PageContainer(cut).HasAttribute("hidden"));

        await cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Cancel")).Click());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindComponents<RepositoryPicker>()));
        Assert.False(PageContainer(cut).HasAttribute("hidden"));

        await cut.InvokeAsync(() => cut.Find(".mud-appbar").QuerySelectorAll("button")
            .Single(button => button.TextContent.Contains("Switch")).Click());
        cut.WaitForAssertion(() => Assert.Single(cut.FindComponents<RepositoryPicker>()));

        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/agents"));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindComponents<RepositoryPicker>()));
    }

    private static AngleSharp.Dom.IElement PageContainer(IRenderedComponent<MainLayout> layout)
    {
        return layout.Find(".mud-main-content .mud-container > div:last-child");
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

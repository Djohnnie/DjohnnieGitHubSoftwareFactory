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
            var links = cut.FindAll(".app-header > .mud-appbar + .desktop-only > nav [role='group'] a");
            Assert.Equal(new[] { "/agent-templates", "/epic-templates", "/feature-templates", "/story-templates", "/bug-templates", "/task-templates" },
                links.Select(link => link.GetAttribute("href")));
            Assert.Equal(new[]
                {
                    "Manage agent templates", "Manage epic templates", "Manage feature templates", "Manage user story templates",
                    "Manage bug templates", "Manage task templates"
                },
                links.Select(link => link.GetAttribute("aria-label")));
            Assert.All(links, link => Assert.Empty(link.TextContent.Trim()));
            var internalAgents = cut.Find(".app-header > .mud-appbar + .desktop-only > nav > a[href='/internal-agents']");
            Assert.Equal("Internal agents", internalAgents.TextContent.Trim());
            Assert.Contains("Select repository", cut.Find(".mud-appbar").TextContent);
        }
        else
        {
            Assert.Empty(cut.FindAll("nav"));
        }
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
                "/", "/copilot-instructions", "/agents", "/agent-templates", "/epics", "/epic-templates", "/features", "/feature-templates",
                "/stories", "/story-templates", "/bugs", "/bug-templates", "/tasks", "/task-templates", "/models", "/internal-agents"
            },
            navigation.QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Equal(new[] { "/agents", "/agent-templates" },
            navigation.QuerySelector("[aria-label='Agents and templates']")!.QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Equal(new[] { "/epics", "/epic-templates" },
            navigation.QuerySelector("[aria-label='Epics and templates']")!.QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Equal(new[] { "/stories", "/story-templates" },
            navigation.QuerySelector("[aria-label='User stories and templates']")!.QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        foreach (var group in navigation.QuerySelectorAll("[role='group']"))
        {
            var templatesLink = group.QuerySelectorAll("a").Last();
            Assert.Empty(templatesLink.TextContent.Trim());
            Assert.StartsWith("Manage ", templatesLink.GetAttribute("aria-label"));
            var separator = group.QuerySelector(".navigation-separator")!;
            Assert.Equal("/", separator.TextContent);
            Assert.Equal("true", separator.GetAttribute("aria-hidden"));
        }
        Assert.All(cut.FindComponents<MudButton>(), button =>
        {
            Assert.Equal(Variant.Text, button.Instance.Variant);
            Assert.Contains("header-link", button.Instance.Class);
        });
        Assert.Equal("submit", header.QuerySelector(".logout button")!.GetAttribute("type"));

        await cut.InvokeAsync(() => header.QuerySelectorAll("button")
            .Single(button => button.TextContent.Contains("Switch")).Click());

        cut.WaitForAssertion(() =>
            Assert.Equal("owner/repo", cut.FindComponent<RepositoryPickerDialog>().Instance.SelectedFullName));
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

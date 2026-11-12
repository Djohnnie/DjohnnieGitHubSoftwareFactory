using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Layout;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class CopilotCodeReviewTests : BunitContext
{
    private const string SettingsPath = "repos/owner/repo/copilot/code-review/settings";
    private const string Inherited = """
        {"review_effort":{"configured":null,"effective":"balanced"},"available_review_efforts":["lite","balanced","max"],
         "severity_filter":"no_filter","custom_instructions_enabled":true,"mcp_enabled":true}
        """;

    private readonly FakeGitHub _gitHub = new();

    public CopilotCodeReviewTests()
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
        Services.AddApplication();
        Services.AddScoped<IGitHubTokenProvider, GitHubTokenProvider>();
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    [Fact]
    public async Task Load_ShowsTheSettingsFromGitHub_AndTheEffortsItOffers()
    {
        await SelectRepositoryAsync();
        _gitHub.Settings = Inherited;

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Equal(CopilotCodeReviewSettings.InheritReviewEffort, Select(cut, "Review effort level").Instance.GetState(x => x.Value)));
        Assert.Equal("no_filter", Select(cut, "Severity filter").Instance.GetState(x => x.Value));
        Assert.True(Switch(cut, "Use custom instructions").Instance.GetState(x => x.Value));
        Assert.True(Switch(cut, "Use MCP servers").Instance.GetState(x => x.Value));
        Assert.Contains("Reviews use Balanced", cut.Markup);
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "https://github.com/owner/repo/settings/copilot/code_review");
        Assert.Equal(
            [CopilotCodeReviewSettings.InheritReviewEffort, "lite", "balanced", "max"],
            await OpenAndReadItemsAsync(cut, "Review effort level"));
        Assert.DoesNotContain(_gitHub.Requests, r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task ChangingASwitch_PatchesOnlyThatSetting_AndShowsWhatGitHubReturned()
    {
        await SelectRepositoryAsync();
        _gitHub.Settings = Inherited;
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.True(Switch(cut, "Use custom instructions").Instance.GetState(x => x.Value)));
        _gitHub.Settings = Inherited.Replace("\"custom_instructions_enabled\":true", "\"custom_instructions_enabled\":false");

        await cut.InvokeAsync(() => Switch(cut, "Use custom instructions").Find("input").Change(false));

        cut.WaitForAssertion(() => Assert.False(Switch(cut, "Use custom instructions").Instance.GetState(x => x.Value)));
        var patch = Assert.Single(_gitHub.Requests, r => r.Method == HttpMethod.Patch);
        Assert.Equal(SettingsPath, patch.Path);
        Assert.Equal("""{"custom_instructions_enabled":false}""", patch.Body);
    }

    [Theory]
    [InlineData("Review effort level", "max", """{"review_effort":"max"}""")]
    [InlineData("Review effort level", CopilotCodeReviewSettings.InheritReviewEffort, """{"review_effort":"inherit"}""")]
    [InlineData("Severity filter", "low_and_medium", """{"severity_filter":"low_and_medium"}""")]
    public async Task ChoosingAnOption_PatchesOnlyThatSetting(string label, string value, string expectedBody)
    {
        await SelectRepositoryAsync();
        _gitHub.Settings = Inherited;
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.NotNull(Select(cut, label).Instance.GetState(x => x.Value)));

        await cut.InvokeAsync(() => Select(cut, label).Instance.ValueChanged.InvokeAsync(value));

        cut.WaitForAssertion(() => Assert.Equal(expectedBody, Assert.Single(_gitHub.Requests, r => r.Method == HttpMethod.Patch).Body));
    }

    [Fact]
    public async Task ARefusedChange_ShowsTheError_AndPutsTheSwitchBack()
    {
        await SelectRepositoryAsync();
        _gitHub.Settings = Inherited;
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.True(Switch(cut, "Use MCP servers").Instance.GetState(x => x.Value)));
        _gitHub.PatchStatus = HttpStatusCode.UnprocessableEntity;

        await cut.InvokeAsync(() => Switch(cut, "Use MCP servers").Find("input").Change(false));

        cut.WaitForAssertion(() => Assert.Contains("Could not update the Copilot code review settings", cut.Markup));
        Assert.Contains("Refused by GitHub", cut.Markup);
        Assert.True(Switch(cut, "Use MCP servers").Find("input").HasAttribute("checked"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task ALoadThatFails_ShowsTheError_AndOnlyMentionsAdminAccessWhenThatCanBeTheCause(HttpStatusCode status, bool mentionsAdmin)
    {
        await SelectRepositoryAsync();
        _gitHub.GetStatus = status;

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Contains("Could not load the Copilot code review settings", cut.Markup));
        Assert.Equal(mentionsAdmin, cut.Markup.Contains("Managing these settings requires administrator access", StringComparison.Ordinal));
        Assert.Empty(cut.FindComponents<MudSwitch<bool>>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Navigation_LinksTheCodeReviewPageJustBeforeModels(bool vertical)
    {
        var cut = Render<AppNavigation>(p => p.Add(n => n.HasRepository, true).Add(n => n.Vertical, vertical));

        var links = cut.FindAll("a").Select(a => a.GetAttribute("href")).ToList();

        Assert.Equal(links.IndexOf("/models") - 1, links.IndexOf("/copilot-code-review"));
        Assert.Equal(links.Count - 3, links.IndexOf("/internal-agents"));
        Assert.Equal(links.Count - 2, links.IndexOf("/internal-models"));
        Assert.Equal(links.Count - 1, links.IndexOf("/sync-log"));
    }

    [Fact]
    public void Navigation_HidesTheCodeReviewPageWithoutARepository()
    {
        var cut = Render<AppNavigation>(p => p.Add(n => n.HasRepository, false));

        Assert.DoesNotContain(cut.FindAll("a"), a => a.GetAttribute("href") == "/copilot-code-review");
    }

    private IRenderedComponent<CopilotCodeReview> RenderPage()
    {
        Render<MudPopoverProvider>();
        return Render<CopilotCodeReview>();
    }

    private static IRenderedComponent<MudSelect<string>> Select(IRenderedComponent<CopilotCodeReview> cut, string label)
    {
        return cut.FindComponents<MudSelect<string>>().Single(s => s.Instance.Label == label);
    }

    private static IRenderedComponent<MudSwitch<bool>> Switch(IRenderedComponent<CopilotCodeReview> cut, string label)
    {
        return cut.FindComponents<MudSwitch<bool>>().Single(s => s.Instance.Label == label);
    }

    private static async Task<IReadOnlyList<string?>> OpenAndReadItemsAsync(IRenderedComponent<CopilotCodeReview> cut, string label)
    {
        var select = Select(cut, label);
        await cut.InvokeAsync(() => select.Instance.OpenMenu());
        return select.FindComponents<MudSelectItem<string>>().Select(i => i.Instance.Value).ToList();
    }

    private Task SelectRepositoryAsync()
    {
        return Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null));
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

        public string Settings { get; set; } = "{}";

        public HttpStatusCode GetStatus { get; set; } = HttpStatusCode.OK;

        public HttpStatusCode PatchStatus { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Requests.Add((request.Method, path, body));

            var status = request.Method == HttpMethod.Patch ? PatchStatus : GetStatus;
            if (path != SettingsPath)
            {
                status = HttpStatusCode.NotFound;
            }

            var json = status == HttpStatusCode.OK ? Settings : """{"message":"Refused by GitHub"}""";
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

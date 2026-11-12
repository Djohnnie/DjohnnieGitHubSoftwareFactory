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
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class ActionsPageTests : BunitContext
{
    private const string Workflows = """
        {"total_count":2,"workflows":[
          {"id":7,"name":"Release","path":".github/workflows/release.yml","state":"active","html_url":"https://github.com/owner/repo/blob/main/.github/workflows/release.yml"},
          {"id":9,"name":"Copilot coding agent","path":"dynamic/copilot-swe-agent/copilot","state":"disabled_manually","html_url":null}]}
        """;

    private const string Runs = """
        {"total_count":1,"workflow_runs":[
          {"id":101,"name":"Release","display_title":"Added sync log","event":"push","path":".github/workflows/release.yml","status":"completed",
           "conclusion":"failure","html_url":"https://github.com/owner/repo/actions/runs/101","created_at":"2026-11-12T10:00:00Z",
           "run_started_at":"2026-11-12T10:00:00Z","updated_at":"2026-11-12T10:03:25Z","head_branch":"main","run_number":42,"run_attempt":2,
           "workflow_id":7,"actor":{"login":"octocat","avatar_url":null}}]}
        """;

    private readonly FakeGitHub _gitHub = new();

    public ActionsPageTests()
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
    public async Task Load_ListsTheWorkflows_AndTheRunsOfAllWorkflows()
    {
        await SelectRepositoryAsync();

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Contains("Added sync log", cut.Markup));
        var workflows = cut.FindComponents<MudListItem<long>>();
        Assert.Equal(["All workflows", "Copilot coding agent", "Release"],
            workflows.Select(i => i.Find(".mud-list-item-text p").TextContent.Replace("Disabled", string.Empty).Trim()));
        Assert.Contains("Disabled", workflows[1].Markup);
        Assert.DoesNotContain("Disabled", workflows[2].Markup);
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "https://github.com/owner/repo/actions/runs/101");
        Assert.Contains("Release #42 (attempt 2)", cut.Markup);
        Assert.Contains("Failure", cut.Markup);
        Assert.Contains("octocat", cut.Markup);
        Assert.Contains("3m 25s", cut.Markup);
        Assert.Contains(_gitHub.Requests, r => r.Path == "repos/owner/repo/actions/runs" && r.Query.Contains("per_page=25") && r.Query.Contains("page=1"));
    }

    [Fact]
    public async Task ChoosingAWorkflow_LoadsOnlyItsRuns()
    {
        await SelectRepositoryAsync();
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.Contains("Added sync log", cut.Markup));

        await cut.InvokeAsync(() => cut.FindComponent<MudList<long>>().Instance.SelectedValueChanged.InvokeAsync(7));

        cut.WaitForAssertion(() => Assert.Contains(_gitHub.Requests, r => r.Path == "repos/owner/repo/actions/workflows/7/runs"));
    }

    [Fact]
    public async Task AWorkflowInTheUrl_LoadsOnlyItsRunsFromTheStart()
    {
        await SelectRepositoryAsync();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/actions?workflow=7");

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Contains("Added sync log", cut.Markup));
        Assert.Equal(7, cut.FindComponent<MudList<long>>().Instance.GetState(x => x.SelectedValue));
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path == "repos/owner/repo/actions/runs");
    }

    [Fact]
    public void Overview_ShowsTheLatestRunPerWorkflow_MostRecentFirst_AndTheOnesThatNeverRanLast()
    {
        var cut = Render<WorkflowRunsInfographic>(p => p.Add(x => x.Repository, Repository()));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        var rows = cut.FindAll("tbody tr");
        Assert.Contains("Release", rows[0].QuerySelector("th")!.TextContent);
        Assert.Equal("/actions?workflow=7", rows[0].QuerySelector("th a")!.GetAttribute("href"));
        Assert.Contains("Failure", rows[0].TextContent);
        Assert.Contains("Added sync log", rows[0].TextContent);
        Assert.Contains("3m 25s", rows[0].TextContent);
        Assert.Contains("Copilot coding agent", rows[1].TextContent);
        Assert.Contains("Disabled", rows[1].TextContent);
        Assert.Contains("No runs yet", rows[1].TextContent);
        // Only the latest run of each workflow.
        Assert.All(_gitHub.Requests.Where(r => r.Path.EndsWith("/runs", StringComparison.Ordinal)), r => Assert.Contains("per_page=1&", r.Query));
        Assert.Equal(2, _gitHub.Requests.Count(r => r.Path.EndsWith("/runs", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public void Overview_WhenGitHubRefuses_ShowsTheError(HttpStatusCode status, bool mentionsAccess)
    {
        _gitHub.Status = status;

        var cut = Render<WorkflowRunsInfographic>(p => p.Add(x => x.Repository, Repository()));

        cut.WaitForAssertion(() => Assert.Contains("Could not load the workflow runs", cut.Markup));
        Assert.Equal(mentionsAccess, cut.Markup.Contains("requires access to the repository's Actions", StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public async Task OverviewPage_ShowsTheWorkflowRunsJustAboveThePullRequests()
    {
        await SelectRepositoryAsync();

        var cut = Render<Home>();

        var sections = cut.FindAll("section[aria-labelledby]").Select(s => s.GetAttribute("aria-labelledby")).ToList();
        Assert.Equal(sections.IndexOf("copilot-sessions-title") - 1, sections.IndexOf("workflow-runs-title"));
        Assert.True(sections.IndexOf("workflow-runs-title") >= 0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task ALoadThatFails_ShowsTheError_AndOnlyMentionsActionsAccessWhenThatCanBeTheCause(HttpStatusCode status, bool mentionsAccess)
    {
        await SelectRepositoryAsync();
        _gitHub.Status = status;

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Contains("Could not load the workflow runs", cut.Markup));
        Assert.Contains("Could not load the workflows", cut.Markup);
        Assert.Equal(mentionsAccess, cut.Markup.Contains("requires access to the repository's Actions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Request_RefusesAnInvalidPageSize_WithoutCallingGitHub()
    {
        var gitHub = Services.GetRequiredService<GitHubService>();

        await Assert.ThrowsAsync<Contracts.ValidationFailedException>(
            () => gitHub.GetWorkflowRunsAsync(new WorkflowRunsRequest("owner/repo", null, 0, WorkflowRunsRequest.MaxPageSize + 1), Xunit.TestContext.Current.CancellationToken));
        Assert.Empty(_gitHub.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Navigation_LinksTheActionsPageRightAfterTheOverview(bool vertical)
    {
        var cut = Render<AppNavigation>(p => p.Add(n => n.HasRepository, true).Add(n => n.Vertical, vertical));

        var links = cut.FindAll("a").Select(a => a.GetAttribute("href")).ToList();

        Assert.Equal(["/", "/actions"], links.Take(2));
    }

    [Fact]
    public void Navigation_HidesTheActionsPageWithoutARepository()
    {
        var cut = Render<AppNavigation>(p => p.Add(n => n.HasRepository, false));

        Assert.DoesNotContain(cut.FindAll("a"), a => a.GetAttribute("href") == "/actions");
    }

    private IRenderedComponent<Actions> RenderPage()
    {
        Render<MudPopoverProvider>();
        return Render<Actions>();
    }

    private Task SelectRepositoryAsync()
    {
        return Services.GetRequiredService<RepositoryContext>().SelectAsync(Repository());
    }

    private static GitHubRepository Repository()
    {
        return new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null);
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<(string Path, string Query)> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Requests.Add((path, request.RequestUri.Query));

            string? json = path switch
            {
                "repos/owner/repo/actions/workflows" => Workflows,
                "repos/owner/repo/actions/runs" or "repos/owner/repo/actions/workflows/7/runs" => Runs,
                "repos/owner/repo/actions/workflows/9/runs" => """{"total_count":0,"workflow_runs":[]}""",
                _ => null
            };
            var status = json is null ? HttpStatusCode.NotFound : Status;
            var body = status == HttpStatusCode.OK ? json! : """{"message":"Refused by GitHub"}""";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

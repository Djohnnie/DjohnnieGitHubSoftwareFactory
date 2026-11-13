using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class RepositoryInfographicTests : BunitContext
{
    private const string Repo = "owner/repo";
    private const string InstructionsUrl = "repos/owner/repo/contents/.github/copilot-instructions.md";
    private const string AgentsUrl = "repos/owner/repo/contents/.github/agents";
    private const string SkillsUrl = "repos/owner/repo/contents/.github/skills";
    private readonly FakeGitHub _gitHub = new();
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;

    public RepositoryInfographicTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        AddAuthorization().SetAuthorized("Djohnnie").SetClaims(new Claim(GitHubClaims.AccessToken, "token"));
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddLogging();
        Services.AddApplication();
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    [Fact]
    public async Task ShowsTheInstructions_TheAgentFiles_TheSkillsAndHowFarEachTypeGot()
    {
        _gitHub.Responses[InstructionsUrl] = """{"path":".github/copilot-instructions.md","sha":"a","content":"IyBIaQ==","encoding":"base64"}""";
        _gitHub.Responses[AgentsUrl] = """
            [{"name":"one.agent.md","path":".github/agents/one.agent.md","sha":"a","type":"file"},
             {"name":"two.md","path":".github/agents/two.md","sha":"b","type":"file"},
             {"name":"notes.txt","path":".github/agents/notes.txt","sha":"c","type":"file"},
             {"name":"more.md","path":".github/agents/more.md","sha":"d","type":"dir"}]
            """;
        _gitHub.Responses[SkillsUrl] = """
            [{"name":"review","path":".github/skills/review","sha":"e","type":"dir"},
             {"name":"release-notes","path":".github/skills/release-notes","sha":"f","type":"dir"},
             {"name":"README.md","path":".github/skills/README.md","sha":"g","type":"file"}]
            """;
        await SeedBugsAsync(Repo);
        await SeedBugsAsync("owner/other");

        var cut = Render<RepositoryInfographic>(p => p.Add(x => x.RepositoryFullName, Repo));

        cut.WaitForAssertion(() => Assert.Contains("Available", cut.Markup));
        Assert.Equal("2", Stat(cut, "Custom agents"));
        Assert.Equal("2", Stat(cut, "Skills"));
        Assert.Equal(["1", "2", "1"], Numbers(cut, "Bugs"));
        Assert.Equal(["0", "0", "0"], Numbers(cut, "Epics"));
        Assert.Equal(WorkItemTypeRules.All.Count, cut.FindAll("tbody tr").Count);
        // The counts come from the listings alone, not from reading every agent or skill file.
        Assert.Equal(3, _gitHub.Requests.Count);

        var track = cut.Find("tbody .infographic-track");
        Assert.Null(track.GetAttribute("style"));
        Assert.Equal("width: 75%", track.QuerySelector(".infographic-accepted")!.GetAttribute("style"));
        Assert.Equal("width: 25%", track.QuerySelector(".infographic-finished")!.GetAttribute("style"));
    }

    [Fact]
    public void WithoutInstructionsAgentsAndSkillsDirectories_ShowsNotAvailableAndNoAgentsOrSkills()
    {
        var cut = Render<RepositoryInfographic>(p => p.Add(x => x.RepositoryFullName, Repo));

        cut.WaitForAssertion(() => Assert.Contains("Not available", cut.Markup));
        Assert.Equal("0", Stat(cut, "Custom agents"));
        Assert.Equal("0", Stat(cut, "Skills"));
        Assert.Empty(cut.FindAll(".infographic-track"));
    }

    [Fact]
    public async Task WhenGitHubFails_StillShowsTheWorkItemCounts()
    {
        _gitHub.Status = HttpStatusCode.BadGateway;
        await SeedBugsAsync(Repo);

        var cut = Render<RepositoryInfographic>(p => p.Add(x => x.RepositoryFullName, Repo));

        cut.WaitForAssertion(() => Assert.Equal(3, Stats(cut).Count(s => s.TextContent.Contains("Could not check"))));
        // The agents and skills share one box, so the boxes stay the same.
        Assert.Equal(2, cut.FindAll(".infographic-stat").Count);
        Assert.DoesNotContain("Not available", cut.Markup);
        Assert.Equal(["1", "2", "1"], Numbers(cut, "Bugs"));
    }

    /// <summary>One bug per status.</summary>
    private async Task SeedBugsAsync(string repository)
    {
        var store = Services.GetRequiredService<IWorkItemStore>();
        var drafted = await SaveBugAsync(store, repository, "Draft");
        Assert.Equal(WorkItemStatus.Draft, drafted.Status);

        var accepted = await SaveBugAsync(store, repository, "Accepted");
        await store.MarkAcceptedAsync(repository, accepted.Id, 1, 1, "https://example.com/1", _cancellationToken);

        var inProgress = await SaveBugAsync(store, repository, "In progress");
        await store.MarkAcceptedAsync(repository, inProgress.Id, 2, 2, "https://example.com/2", _cancellationToken);
        await store.MarkAssignedAsync(repository, inProgress.Id, _cancellationToken);

        var done = await SaveBugAsync(store, repository, "Done");
        await store.MarkAcceptedAsync(repository, done.Id, 3, 3, "https://example.com/3", _cancellationToken);
        await store.ApplyIssueAsync(repository, done.Id, new WorkItemIssueSnapshot(3, "https://example.com/3", "Done", string.Empty, true, false), _cancellationToken);
    }

    private Task<WorkItem> SaveBugAsync(IWorkItemStore store, string repository, string title)
    {
        return store.SaveAsync(new WorkItem { RepositoryFullName = repository, Type = WorkItemType.Bug, Title = title }, _cancellationToken);
    }

    private static string Stat(IRenderedComponent<RepositoryInfographic> cut, string label)
    {
        var stat = Stats(cut).Single(s => s.TextContent.Contains(label));
        return stat.QuerySelector("h6")!.TextContent.Trim();
    }

    /// <summary>The label and value of every stat; a box with two counts has a part per count.</summary>
    private static IEnumerable<AngleSharp.Dom.IElement> Stats(IRenderedComponent<RepositoryInfographic> cut)
    {
        return cut.FindAll(".infographic-stat > div:not(.infographic-split), .infographic-part");
    }

    private static string[] Numbers(IRenderedComponent<RepositoryInfographic> cut, string pluralName)
    {
        var row = cut.FindAll("tbody tr").Single(r => r.QuerySelector("th")!.TextContent.Contains(pluralName));
        return row.QuerySelectorAll("td.infographic-number").Select(c => c.TextContent.Trim()).ToArray();
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        /// <summary>The JSON per path; other paths are not found.</summary>
        public Dictionary<string, string> Responses { get; } = [];

        /// <summary>When set, every request fails with this status.</summary>
        public HttpStatusCode? Status { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath.TrimStart('/'));
            lock (Requests)
            {
                Requests.Add(path);
            }

            if (Status is { } status)
            {
                return Task.FromResult(new HttpResponseMessage(status));
            }
            return Task.FromResult(Responses.TryGetValue(path, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

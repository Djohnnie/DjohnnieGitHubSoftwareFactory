using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
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

public class CopilotSessionsInfographicTests : BunitContext
{
    private const string Repo = "me/repo";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeGitHub _gitHub = new();
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;

    public CopilotSessionsInfographicTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        AddAuthorization().SetAuthorized("Djohnnie").SetClaims(new Claim(GitHubClaims.AccessToken, "token"));
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton<TimeProvider>(new FixedTime(Now));
        Services.AddLogging();
        Services.AddApplication();
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    [Fact]
    public async Task Overview_CountsTheOpenPullRequestsAndMatchesCopilotsSessionsAndWorkItems()
    {
        var bug = await SeedBugWithPullRequestAsync(12);
        _gitHub.PullRequests.Add(PullRequest(12, "Fix the login bug", "Copilot", "Bot", draft: true, branch: "copilot/fix-login", updated: Now.AddMinutes(-5)));
        _gitHub.PullRequests.Add(PullRequest(11, "Add dark mode", "Copilot", "Bot", draft: false, branch: "copilot/dark-mode", updated: Now.AddHours(-3)));
        _gitHub.PullRequests.Add(PullRequest(9, "Update the readme", "someone", "User", draft: false, branch: "readme", updated: Now.AddDays(-3)));
        _gitHub.Runs["in_progress"] = [Run(1, "in_progress", "copilot/fix-login", Now.AddMinutes(-42)), Run(2, "in_progress", "copilot/new-thing", Now.AddMinutes(-2)),
            Run(3, "in_progress", "readme", Now.AddMinutes(-1), copilot: false)];
        _gitHub.Runs["queued"] = [Run(4, "queued", "copilot/dark-mode", Now.AddMinutes(-1))];

        var cut = Render<CopilotSessionsInfographic>(p => p.Add(x => x.Repository, Repository()));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));
        Assert.Equal("3", Stat(cut, "Open pull requests"));
        Assert.Contains("1 draft", cut.Markup);
        Assert.Equal("2", Stat(cut, "Opened by Copilot agent"));
        Assert.Equal("0", Stat(cut, "Opened by DGHSF agent"));
        // The CI run on 'readme' isn't Copilot's; the session on 'copilot/new-thing' has no pull request yet.
        Assert.Equal("2", Stat(cut, "Copilot agent running"));
        Assert.Equal("0", Stat(cut, "DGHSF agent running"));
        Assert.Contains("1 queued", cut.Markup);
        Assert.Contains("Pull requests &amp; Agent sessions", cut.Markup);

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(3, rows.Count);
        Assert.Contains("Running for 42 min", rows[0].TextContent);
        Assert.Contains("Fix the login bug", rows[0].TextContent);
        Assert.Contains("Copilot agent", rows[0].TextContent);
        Assert.Contains("Copilot working", rows[0].TextContent);
        Assert.Contains(bug.Title, rows[0].TextContent);
        Assert.NotNull(rows[0].QuerySelector($"a[href='/bugs/{bug.Id}']"));
        Assert.Contains("Queued", rows[1].TextContent);
        Assert.Contains("Copilot agent", rows[1].TextContent);
        Assert.Contains("Open", rows[1].TextContent);
        // A person opened the readme pull request: no agent and no session.
        Assert.DoesNotContain("agent", rows[2].TextContent);
        Assert.Contains("—", rows[2].TextContent);
        Assert.Contains("Copilot sessions without an open pull request", cut.Markup);
        Assert.Contains("copilot/new-thing", cut.Markup);
    }

    [Fact]
    public async Task Overview_TellsTheDghsfAgentWorkersPullRequestsAndRunsApartFromCopilots()
    {
        var bug = await SeedBugWithPullRequestAsync(12);
        var task = await SeedBugWithPullRequestAsync(14, WorkItemType.Task, "Upgrade .NET");
        var runs = Services.GetRequiredService<IAgentRunStore>();
        // A revision run on the worker's pull request #12, and a first run on an item without a pull request yet.
        await runs.CreateRevisionAsync(Repo, bug.Id, null, 1, "dghsf/login", 12, $"https://github.com/{Repo}/pull/12", _cancellationToken);
        var crash = await Services.GetRequiredService<IWorkItemStore>().SaveAsync(
            new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "The app crashes on start" }, _cancellationToken);
        await runs.CreateAsync(Repo, crash.Id, null, 1, _cancellationToken);
        _gitHub.PullRequests.Add(PullRequest(12, "Fix the login bug", "Djohnnie", "User", draft: false, branch: "dghsf/login", updated: Now));
        _gitHub.PullRequests.Add(PullRequest(14, "Upgrade .NET", "Copilot", "Bot", draft: false, branch: "copilot/upgrade", updated: Now.AddHours(-1)));

        var cut = Render<CopilotSessionsInfographic>(p => p.Add(x => x.Repository, Repository()));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        Assert.Equal("1", Stat(cut, "Opened by DGHSF agent"));
        Assert.Equal("1", Stat(cut, "Opened by Copilot agent"));
        Assert.Equal("0", Stat(cut, "DGHSF agent running"));
        Assert.Contains("2 queued", cut.Markup);

        var rows = cut.FindAll("tbody tr");
        Assert.Contains("DGHSF agent", rows[0].TextContent);
        Assert.Contains("Agent working", rows[0].TextContent);
        Assert.Contains("Queued", rows[0].TextContent);
        Assert.Contains("Copilot agent", rows[1].TextContent);
        Assert.Contains(task.Title, rows[1].TextContent);
        Assert.Contains("DGHSF agent runs without an open pull request", cut.Markup);
        Assert.NotNull(cut.Find($"a[href='/bugs/{crash.Id}']"));
    }

    [Fact]
    public void Overview_WithoutActionsAccess_ShowsThePullRequestsButNoSessionCount()
    {
        _gitHub.PullRequests.Add(PullRequest(3, "Something", "Copilot", "Bot", draft: false, branch: "copilot/x", updated: Now));
        _gitHub.RunsStatus = HttpStatusCode.Forbidden;

        var cut = Render<CopilotSessionsInfographic>(p => p.Add(x => x.Repository, Repository()));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Equal("?", Stat(cut, "Copilot agent running"));
        Assert.Contains("needs access to Actions", cut.Markup);
    }

    [Fact]
    public void Overview_WhenThePullRequestsCantBeRead_SaysSo()
    {
        _gitHub.PullsStatus = HttpStatusCode.NotFound;

        var cut = Render<CopilotSessionsInfographic>(p => p.Add(x => x.Repository, Repository()));

        cut.WaitForAssertion(() => Assert.Contains("Could not load the open pull requests", cut.Markup));
    }

    [Fact]
    public void Overview_CheckAgain_ReadsGitHubAgain()
    {
        var cut = Render<CopilotSessionsInfographic>(p => p.Add(x => x.Repository, Repository()));
        cut.WaitForAssertion(() => Assert.Contains("There are no open pull requests.", cut.Markup));
        _gitHub.PullRequests.Add(PullRequest(5, "New one", "Copilot", "Bot", draft: true, branch: "copilot/new", updated: Now));

        cut.Find("button[aria-label='Check the pull requests and sessions again']").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Equal(2, _gitHub.PullRequestListings);
    }

    private async Task<WorkItem> SeedBugWithPullRequestAsync(int pullRequestNumber, WorkItemType type = WorkItemType.Bug, string title = "Login fails")
    {
        var store = Services.GetRequiredService<IWorkItemStore>();
        var item = await store.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = type, Title = title }, _cancellationToken);
        await store.MarkAcceptedAsync(Repo, item.Id, pullRequestNumber, pullRequestNumber, $"https://example.com/{pullRequestNumber}", _cancellationToken);
        return await store.ApplyIssueAsync(Repo, item.Id, new WorkItemIssueSnapshot(pullRequestNumber, $"https://example.com/{pullRequestNumber}", title, string.Empty, false, true,
            new WorkItemProgressSnapshot(CopilotProgress.Working, pullRequestNumber, $"https://github.com/{Repo}/pull/{pullRequestNumber}")), _cancellationToken);
    }

    /// <summary>The number in the stat box (or, in a box split per agent, in the half) with the label.</summary>
    private static string Stat(IRenderedComponent<CopilotSessionsInfographic> cut, string label)
    {
        var stat = cut.FindAll(".infographic-part").SingleOrDefault(s => s.TextContent.Contains(label))
            ?? cut.FindAll(".infographic-stat").Single(s => s.QuerySelector(".infographic-part") is null && s.TextContent.Contains(label));
        return stat.QuerySelectorAll("h6").Single().TextContent.Trim();
    }

    private static GitHubRepository Repository()
    {
        return new GitHubRepository(1, "repo", Repo, new GitHubOwner("me", null, "User"), $"https://github.com/{Repo}", null, true, false, false, null, "main", null);
    }

    private static object PullRequest(int number, string title, string author, string authorType, bool draft, string branch, DateTimeOffset updated)
    {
        return new
        {
            number, title, draft, html_url = $"https://github.com/{Repo}/pull/{number}", user = new { login = author, type = authorType },
            created_at = updated.AddHours(-1), updated_at = updated, merged_at = (DateTimeOffset?)null, head = new { @ref = branch }
        };
    }

    private static object Run(long id, string status, string branch, DateTimeOffset started, bool copilot = true)
    {
        return new
        {
            id, name = copilot ? "Running Copilot cloud agent" : "CI", @event = copilot ? "dynamic" : "pull_request",
            path = copilot ? "dynamic/copilot-swe-agent/copilot" : ".github/workflows/ci.yml",
            status, conclusion = (string?)null, html_url = $"https://github.com/{Repo}/actions/runs/{id}", created_at = started, head_branch = branch, run_started_at = started
        };
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<object> PullRequests { get; } = [];
        public Dictionary<string, List<object>> Runs { get; } = [];
        public HttpStatusCode PullsStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode RunsStatus { get; set; } = HttpStatusCode.OK;
        public int PullRequestListings { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            if (path == $"/repos/{Repo}/pulls")
            {
                PullRequestListings++;
                return Task.FromResult(PullsStatus == HttpStatusCode.OK
                    ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(PullRequests))
                    : Json(PullsStatus, """{"message":"Not Found"}"""));
            }
            if (path == $"/repos/{Repo}/actions/runs")
            {
                return Task.FromResult(RunsStatus == HttpStatusCode.OK
                    ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { workflow_runs = Runs.GetValueOrDefault(query["status"] ?? string.Empty, []) }))
                    : Json(RunsStatus, """{"message":"Resource not accessible by integration"}"""));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
        {
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

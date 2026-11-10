using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class CopilotUsageInfographicTests : BunitContext
{
    private const string ReportsPath = "orgs/org/copilot/metrics/reports/repos-1-day";
    private const string DownloadHost = "reports.example.com";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeGitHub _gitHub = new();

    public CopilotUsageInfographicTests()
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
    public void Activity_AddsUpTheRepositorysRowsOfTheLast28Days()
    {
        // The reports cover the whole organization; only org/repo's rows count.
        _gitHub.Days[new DateOnly(2026, 11, 2)] = [Row("org", "repo", created: 4, byCopilot: 3, merged: 2, mergedByCopilot: 1, reviewed: 2, evaluated: 5, addressed: 3, high: (2, 2)), Row("org", "other", 9, 9, 9, 9, 9, 9, 9, (9, 9))];
        _gitHub.Days[new DateOnly(2026, 10, 13)] = [Row("Org", "Repo", 1, 1, 1, 1, 1, 2, 1, (2, 1))];
        // Before the period: not requested.
        _gitHub.Days[new DateOnly(2026, 10, 12)] = [Row("org", "repo", 100, 100, 100, 100, 100, 100, 100, (100, 100))];

        var cut = Render<CopilotActivityInfographic>(p => p.Add(x => x.Repository, Repository("org/repo", "Organization")));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".usage-day")));
        Assert.Equal("4", Stat(cut, "PRs created by Copilot"));
        Assert.Contains("of 5 created", cut.Markup);
        Assert.Equal("2", Stat(cut, "Copilot PRs merged"));
        Assert.Equal("3", Stat(cut, "PRs reviewed by Copilot"));
        Assert.Equal("4", Stat(cut, "Review comments addressed"));
        Assert.Contains("Oct 13 – Nov 9", cut.Markup);
        Assert.Equal(28, cut.FindAll(".usage-day").Count);
        Assert.Equal(["4", "3"], cut.FindAll("tbody td.infographic-number").Select(c => c.TextContent.Trim()));
        Assert.Equal(28, _gitHub.ReportRequests.Count);
        Assert.DoesNotContain(new DateOnly(2026, 10, 12), _gitHub.ReportRequests);
        // The signed report links never get the GitHub token, and the metrics endpoints use their documented API version.
        Assert.All(_gitHub.DownloadAuthorizations, Assert.Null);
        Assert.Equal(GitHubApiClient.UsageApiVersion, _gitHub.ReportApiVersion);
    }

    [Fact]
    public void Activity_ReusesTheCachedReportsOfTheOrganization()
    {
        _gitHub.Days[new DateOnly(2026, 11, 2)] = [Row("org", "repo", 1, 1, 0, 0, 0, 0, 0, null), Row("org", "other", 2, 2, 0, 0, 0, 0, 0, null)];

        var first = Render<CopilotActivityInfographic>(p => p.Add(x => x.Repository, Repository("org/repo", "Organization")));
        first.WaitForAssertion(() => Assert.Equal("1", Stat(first, "PRs created by Copilot")));
        var other = Render<CopilotActivityInfographic>(p => p.Add(x => x.Repository, Repository("org/other", "Organization")));

        other.WaitForAssertion(() => Assert.Equal("2", Stat(other, "PRs created by Copilot")));
        Assert.Equal(28, _gitHub.ReportRequests.Count);
    }

    [Fact]
    public void Activity_ToleratesReportsThatAreNotPublishedYet()
    {
        _gitHub.NotFoundDays.UnionWith([new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 9)]);
        _gitHub.Days[new DateOnly(2026, 11, 6)] = [Row("org", "repo", 1, 1, 0, 0, 0, 0, 0, null)];

        var cut = Render<CopilotActivityInfographic>(p => p.Add(x => x.Repository, Repository("org/repo", "Organization")));

        cut.WaitForAssertion(() => Assert.Equal("1", Stat(cut, "PRs created by Copilot")));
    }

    [Fact]
    public void Activity_WithoutAccess_ExplainsWhatItNeeds()
    {
        _gitHub.NotFoundDays.Add(new DateOnly(2026, 10, 20));

        var cut = Render<CopilotActivityInfographic>(p => p.Add(x => x.Repository, Repository("org/repo", "Organization")));

        cut.WaitForAssertion(() => Assert.Contains("Could not load the Copilot usage metrics", cut.Markup));
        Assert.Contains("read:org", cut.Markup);
    }

    [Fact]
    public void Activity_ForAPersonalRepository_WorksItOutFromThePullRequests()
    {
        const string copilot = """{"login":"Copilot","type":"Bot"}""";
        const string reviewer = """{"login":"copilot-pull-request-reviewer[bot]","type":"Bot"}""";
        const string me = """{"login":"me","type":"User"}""";
        _gitHub.Responses["repos/me/repo/pulls"] = $$"""
            [{"number":3,"user":{{me}},"created_at":"2026-11-04T10:00:00Z","updated_at":"2026-11-06T10:00:00Z","merged_at":null},
             {"number":2,"user":{{me}},"created_at":"2026-10-01T10:00:00Z","updated_at":"2026-11-05T10:00:00Z","merged_at":"2026-11-05T10:00:00Z"},
             {"number":1,"user":{{copilot}},"created_at":"2026-11-02T10:00:00Z","updated_at":"2026-11-03T10:00:00Z","merged_at":"2026-11-03T10:00:00Z"},
             {"number":4,"user":{{copilot}},"created_at":"2026-09-01T10:00:00Z","updated_at":"2026-10-01T10:00:00Z","merged_at":"2026-10-01T10:00:00Z"}]
            """;
        _gitHub.Responses["repos/me/repo/pulls/1/reviews"] = $$"""[{"user":{{reviewer}},"submitted_at":"2026-11-02T11:00:00Z"}]""";
        _gitHub.Responses["repos/me/repo/pulls/2/reviews"] = $$"""[{"user":{{me}},"submitted_at":"2026-11-05T09:00:00Z"}]""";
        // Two Copilot reviews of one pull request count once.
        _gitHub.Responses["repos/me/repo/pulls/3/reviews"] = $$"""
            [{"user":{{reviewer}},"submitted_at":"2026-11-04T11:00:00Z"},{"user":{{reviewer}},"submitted_at":"2026-11-06T11:00:00Z"}]
            """;

        var cut = Render<CopilotActivityInfographic>(p => p.Add(x => x.Repository, Repository("me/repo", "User")));

        cut.WaitForAssertion(() => Assert.Equal("1", Stat(cut, "PRs created by Copilot")));
        Assert.Contains("of 2 created", cut.Markup);
        Assert.Equal("1", Stat(cut, "Copilot PRs merged"));
        Assert.Contains("of 2 merged", cut.Markup);
        Assert.Equal("2", Stat(cut, "PRs reviewed by Copilot"));
        Assert.Contains("no Copilot usage metrics for personal accounts", cut.Markup);
        Assert.DoesNotContain("Review comments addressed", cut.Markup);
        Assert.DoesNotContain("Copilot code review comments", cut.Markup);
        Assert.Equal(28, cut.FindAll(".usage-day").Count);
        // No usage metrics reports, and no reviews of the pull request that wasn't updated in the period.
        Assert.Empty(_gitHub.ReportRequests);
        Assert.DoesNotContain("repos/me/repo/pulls/4/reviews", _gitHub.Requests);
    }

    [Fact]
    public void Billing_ShowsBothReportsPerModelForTheOrganization()
    {
        _gitHub.Responses["organizations/org/settings/billing/premium_request/usage"] = Usage(("Claude Opus 5.5", 120, 4.8m, 2m), ("GPT-5", 10, 0.4m, 0.4m));
        _gitHub.Responses["organizations/org/settings/billing/ai_credit/usage"] = Usage(("Claude Opus 5.5", 300, 3m, 0m));

        var cut = Render<CopilotBillingInfographic>(p => p.Add(x => x.Repository, Repository("org/repo", "Organization")));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));
        Assert.Equal("130", Stat(cut, "Premium requests"));
        Assert.Equal("300", Stat(cut, "AI credits"));
        Assert.Equal("$2.40", Stat(cut, "Billed"));
        Assert.Equal("$5.80", Stat(cut, "Included in the plan"));
        Assert.Contains("November 2026", cut.Markup);
        var rows = cut.FindAll("tbody tr");
        Assert.Equal(["Claude Opus 5.5", "GPT-5"], rows.Select(r => r.QuerySelector("th")!.TextContent.Trim()));
        Assert.Equal(["120", "300", "$5.80", "$2.00"], rows[0].QuerySelectorAll("td.infographic-number").Select(c => c.TextContent.Trim()));
        Assert.Equal(["10", "–", "$0.00", "$0.40"], rows[1].QuerySelectorAll("td.infographic-number").Select(c => c.TextContent.Trim()));
        Assert.Contains(_gitHub.Queries, q => q == "?year=2026&month=11");
    }

    [Fact]
    public void Billing_OfAPersonalRepository_UsesTheUserReports_AndShowsTheOneThatLoaded()
    {
        _gitHub.Responses["users/me/settings/billing/premium_request/usage"] = Usage(("GPT-5", 7, 0.28m, 0m));

        var cut = Render<CopilotBillingInfographic>(p => p.Add(x => x.Repository, Repository("me/repo", "User")));

        cut.WaitForAssertion(() => Assert.Contains("Could not load the AI credit usage", cut.Markup));
        Assert.Equal("7", Stat(cut, "Premium requests"));
        Assert.Equal("Could not load", cut.FindAll(".infographic-stat").Single(s => s.TextContent.Contains("AI credits")).QuerySelector("p:last-child")!.TextContent.Trim());
        Assert.Equal("$0.00", Stat(cut, "Billed"));
    }

    private static string Stat<T>(IRenderedComponent<T> cut, string label) where T : Microsoft.AspNetCore.Components.IComponent
    {
        var stat = cut.FindAll(".infographic-stat").Single(s => s.TextContent.Contains(label));
        return stat.QuerySelector("h6")!.TextContent.Trim();
    }

    private static GitHubRepository Repository(string fullName, string ownerType)
    {
        var owner = fullName.Split('/')[0];
        return new GitHubRepository(1, fullName.Split('/')[1], fullName, new GitHubOwner(owner, null, ownerType),
            $"https://github.com/{fullName}", null, true, false, false, null, "main", null);
    }

    private static string Row(string owner, string name, int created, int byCopilot, int merged, int mergedByCopilot, int reviewed, int evaluated, int addressed, (int Evaluated, int Addressed)? high)
    {
        var severities = high is { } h ? $$"""[{"severity":"high","total_comments_evaluated":{{h.Evaluated}},"total_comments_addressed":{{h.Addressed}}}]""" : "[]";
        return $$"""{"day":"2026-11-01","repo_id":1,"repo_owner_name":"{{owner}}","repo_name":"{{name}}","repo_visibility":"PRIVATE","pull_requests":{"total_created":{{created}},"total_created_by_copilot":{{byCopilot}},"total_merged":{{merged}},"total_merged_created_by_copilot":{{mergedByCopilot}},"total_reviewed_by_copilot":{{reviewed}},"median_minutes_to_merge":12.5,"copilot_suggestions_by_comment_type":[]},"pull_request_review_times":[],"copilot_code_review":{"total_comments_evaluated":{{evaluated}},"total_comments_addressed":{{addressed}},"review_comments_by_severity":{{severities}},"review_comments_by_review_tier":[]}""" + "}";
    }

    private static string Usage(params (string Model, decimal Quantity, decimal Gross, decimal Net)[] items)
    {
        var json = items.Select(i => FormattableString.Invariant($$"""{"product":"copilot","sku":"copilot_premium_request","model":"{{i.Model}}","unitType":"requests","pricePerUnit":0.04,"grossQuantity":{{i.Quantity}},"grossAmount":{{i.Gross}},"discountQuantity":0,"discountAmount":{{i.Gross - i.Net}},"netQuantity":{{i.Quantity}},"netAmount":{{i.Net}}}"""));
        return $$"""{"timePeriod":{"year":2026,"month":11},"usageItems":[{{string.Join(",", json)}}]}""";
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
        private readonly Lock _lock = new();

        public List<string> Requests { get; } = [];
        public List<string> Queries { get; } = [];
        public List<DateOnly> ReportRequests { get; } = [];
        public List<string?> DownloadAuthorizations { get; } = [];
        public string? ReportApiVersion { get; private set; }

        /// <summary>The NDJSON rows of each day's report; other days have no report (204).</summary>
        public Dictionary<DateOnly, string[]> Days { get; } = [];

        /// <summary>Days whose report GitHub answers with 404.</summary>
        public HashSet<DateOnly> NotFoundDays { get; } = [];

        /// <summary>The JSON per API path; other paths are not found.</summary>
        public Dictionary<string, string> Responses { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            lock (_lock)
            {
                Requests.Add(path);
                Queries.Add(uri.Query);
                if (uri.Host == DownloadHost)
                {
                    DownloadAuthorizations.Add(request.Headers.Authorization?.ToString());
                    var day = DateOnly.Parse(path, System.Globalization.CultureInfo.InvariantCulture);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Join("\n", Days[day]) + "\n") });
                }

                if (path == ReportsPath)
                {
                    var day = DateOnly.Parse(uri.Query["?day=".Length..], System.Globalization.CultureInfo.InvariantCulture);
                    ReportRequests.Add(day);
                    ReportApiVersion = string.Join(",", request.Headers.GetValues(GitHubApiClient.ApiVersionHeader));
                    if (NotFoundDays.Contains(day))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                    }
                    return Task.FromResult(Days.ContainsKey(day)
                        ? Json($$"""{"download_links":["https://{{DownloadHost}}/{{day:yyyy-MM-dd}}"],"report_day":"{{day:yyyy-MM-dd}}"}""")
                        : new HttpResponseMessage(HttpStatusCode.NoContent));
                }

                return Task.FromResult(Responses.TryGetValue(path, out var json) ? Json(json) : new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private static HttpResponseMessage Json(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

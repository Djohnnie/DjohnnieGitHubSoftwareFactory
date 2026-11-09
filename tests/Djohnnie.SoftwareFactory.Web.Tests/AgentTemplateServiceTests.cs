using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class AgentTemplateServiceTests : IDisposable
{
    private const string Path = "/repos/{0}/contents/.github/agents/reviewer.agent.md";
    private readonly FakeGitHub _gitHub = new();
    private readonly AgentTemplateService _service;
    private readonly ServiceProvider _services;

    public AgentTemplateServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var client = new GitHubApiClient(new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") }, new GitHubTokenProvider(new TokenAuth()));
        _services = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions))
            .AddSingleton(client).BuildServiceProvider();
        _service = _services.GetRequiredService<AgentTemplateService>();
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    [Fact]
    public async Task Save_StoresTheAgentFileAndRejectsADuplicateFileName()
    {
        var template = await _service.SaveAsync(Guid.Empty, "reviewer", new AgentDefinition { Description = "Reviews code.", Prompt = "Review carefully." }, TestContext.Current.CancellationToken);

        Assert.Equal("reviewer", template.Name);
        Assert.Equal("---\ndescription: Reviews code.\n---\n\nReview carefully.\n", template.Content);
        var error = await Assert.ThrowsAsync<DomainRuleException>(() =>
            _service.SaveAsync(Guid.Empty, "reviewer", new AgentDefinition { Name = "Other", Description = "Other." }, TestContext.Current.CancellationToken));
        Assert.Contains("already uses the file name 'reviewer'", error.Message);

        var renamed = await _service.SaveAsync(template.Id, "reviewer", new AgentDefinition { Name = "Code Reviewer", Description = "Reviews code." }, TestContext.Current.CancellationToken);
        Assert.Equal("Code Reviewer", Assert.Single(await _service.GetAllAsync(TestContext.Current.CancellationToken)).Name);
        Assert.Equal(template.Id, renamed.Id);
    }

    [Fact]
    public async Task Apply_CreatesMissingAgentsAndOnlyReplacesDifferentFilesWhenAllowed()
    {
        var template = await _service.SaveAsync(Guid.Empty, "reviewer", new AgentDefinition { Description = "Reviews code.", Prompt = "Review carefully." }, TestContext.Current.CancellationToken);
        _gitHub.Files["o/same"] = template.Content.Replace("\n", "\r\n");
        _gitHub.Files["o/different"] = "---\ndescription: Old.\n---\n";
        _gitHub.Failing.Add("o/broken");

        var results = await _service.ApplyAsync(template, ["o/new", "o/same", "o/different", "o/broken"], "reviewer", overwrite: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [AgentTemplateApplyOutcome.Created, AgentTemplateApplyOutcome.Unchanged, AgentTemplateApplyOutcome.Skipped, AgentTemplateApplyOutcome.Failed],
            results.Select(r => r.Outcome));
        Assert.Contains("500", results[3].Message);
        var created = Assert.Single(_gitHub.Requests, r => r.Method == "PUT");
        Assert.Equal(string.Format(Path, "o/new"), created.Path);
        using (var body = JsonDocument.Parse(created.Body!))
        {
            Assert.Equal("Create custom agent reviewer from template", body.RootElement.GetProperty("message").GetString());
            Assert.Equal(template.Content, Encoding.UTF8.GetString(Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!)));
            Assert.False(body.RootElement.TryGetProperty("sha", out _));
        }

        var replaced = Assert.Single(await _service.ApplyAsync(template, ["o/different"], "reviewer", overwrite: true, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(AgentTemplateApplyOutcome.Updated, replaced.Outcome);
        using var update = JsonDocument.Parse(_gitHub.Requests.Last(r => r.Method == "PUT").Body!);
        Assert.Equal("sha-o/different", update.RootElement.GetProperty("sha").GetString());
    }

    private sealed class TokenAuth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(GitHubClaims.AccessToken, "token")], "test"))));
        }
    }

    private sealed record Request(string Method, string Path, string? Body);

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];

        /// <summary>The existing agent file per repository.</summary>
        public Dictionary<string, string> Files { get; } = [];

        public HashSet<string> Failing { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new Request(request.Method.Method, path, body));

            var repository = string.Join('/', path.Split('/')[2..4]);
            if (Failing.Contains(repository))
            {
                return Json(HttpStatusCode.InternalServerError, """{"message":"Boom"}""");
            }

            if (request.Method == HttpMethod.Put)
            {
                return Json(HttpStatusCode.OK, """{"content":{"sha":"blob"},"commit":{"sha":"1234567890","html_url":"https://github.com/c"}}""");
            }

            return Files.TryGetValue(repository, out var content)
                ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["path"] = ".github/agents/reviewer.agent.md",
                    ["sha"] = $"sha-{repository}",
                    ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
                }))
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
        {
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

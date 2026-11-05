using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.Epics;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using MediatR;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class EpicServiceTests : IDisposable
{
    private const string Repo = "owner/repo";
    private readonly FakeGitHub _gitHub = new();
    private readonly EpicService _service;
    private readonly ServiceProvider _services;

    public EpicServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var client = new GitHubApiClient(new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") }, new GitHubTokenProvider(new TokenAuth()));
        _services = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions))
            .AddSingleton(client).BuildServiceProvider();
        _service = _services.GetRequiredService<EpicService>();
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    [Fact]
    public async Task Accept_CreatesLabelledIssueAndAssignsCopilotWithAgentAndModel()
    {
        var epic = await _service.SaveAsync(new Epic
        {
            RepositoryFullName = Repo,
            Title = " Login with passkeys ",
            Description = "As a user I want passkeys.",
            CustomAgent = "backend-dev",
            Model = "claude-sonnet-4.6",
            BaseBranch = ""
        }, TestContext.Current.CancellationToken);

        var result = await _service.AcceptAsync(Repo, epic.Id, TestContext.Current.CancellationToken);

        Assert.Null(result.AssignmentError);
        Assert.Equal(WorkItemStatus.InProgress, result.Epic.Status);
        Assert.Equal(42, result.Epic.IssueNumber);
        Assert.Equal("https://github.com/owner/repo/issues/42", result.Epic.IssueUrl);
        Assert.NotNull(result.Epic.AssignedAt);

        Assert.Contains(_gitHub.Requests, r => r.Method == "POST" && r.Path == "/repos/owner/repo/labels");
        var issue = JsonDocument.Parse(_gitHub.Requests.Single(r => r.Path == "/repos/owner/repo/issues").Body!).RootElement;
        Assert.Equal("Login with passkeys", issue.GetProperty("title").GetString());
        Assert.Contains($"<!-- dghsf:epic:{epic.Id} -->", issue.GetProperty("body").GetString());
        Assert.Equal("epic", issue.GetProperty("labels")[0].GetString());

        var assign = JsonDocument.Parse(_gitHub.Requests.Single(r => r.Path == "/repos/owner/repo/issues/42/assignees").Body!).RootElement;
        Assert.Equal("copilot-swe-agent[bot]", assign.GetProperty("assignees")[0].GetString());
        var agent = assign.GetProperty("agent_assignment");
        Assert.Equal(Repo, agent.GetProperty("target_repo").GetString());
        Assert.Equal("backend-dev", agent.GetProperty("custom_agent").GetString());
        Assert.Equal("claude-sonnet-4.6", agent.GetProperty("model").GetString());
        Assert.False(agent.TryGetProperty("base_branch", out _));
    }

    [Fact]
    public async Task Accept_WhenCopilotIsNotAssigned_KeepsIssueAndAllowsRetry()
    {
        _gitHub.CopilotAssignable = false;
        var epic = await _service.SaveAsync(new Epic { RepositoryFullName = Repo, Title = "Epic", Description = "Body" }, TestContext.Current.CancellationToken);

        var result = await _service.AcceptAsync(Repo, epic.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(result.AssignmentError);
        Assert.Equal(WorkItemStatus.Accepted, result.Epic.Status);
        Assert.Equal(42, result.Epic.IssueNumber);

        _gitHub.CopilotAssignable = true;
        var retry = await _service.AssignAgentAsync(Repo, epic.Id, TestContext.Current.CancellationToken);

        Assert.Null(retry.AssignmentError);
        Assert.Equal(WorkItemStatus.InProgress, retry.Epic.Status);
        Assert.Single(_gitHub.Requests, r => r.Path == "/repos/owner/repo/issues");
    }

    [Fact]
    public async Task Accept_WithoutDescription_Throws()
    {
        var epic = await _service.SaveAsync(new Epic { RepositoryFullName = Repo, Title = "Epic" }, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AcceptAsync(Repo, epic.Id, TestContext.Current.CancellationToken));
        Assert.Empty(_gitHub.Requests);
    }

    [Fact]
    public async Task Save_AfterAccept_OnlyChangesAssignment()
    {
        _gitHub.CopilotAssignable = false;
        var epic = await _service.SaveAsync(new Epic { RepositoryFullName = Repo, Title = "Epic", Description = "Body" }, TestContext.Current.CancellationToken);
        var accepted = (await _service.AcceptAsync(Repo, epic.Id, TestContext.Current.CancellationToken)).Epic.Copy();

        accepted.Title = "Changed";
        accepted.Model = "gpt-5";
        await _service.SaveAsync(accepted, TestContext.Current.CancellationToken);

        var stored = await _service.GetAsync(Repo, epic.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Epic", stored!.Title);
        Assert.Equal("gpt-5", stored.Model);
    }

    [Fact]
    public async Task GetAll_OnlyReturnsEpicsOfTheRepository()
    {
        await _service.SaveAsync(new Epic { RepositoryFullName = Repo, Title = "Mine" }, TestContext.Current.CancellationToken);
        await _service.SaveAsync(new Epic { RepositoryFullName = "other/repo", Title = "Other" }, TestContext.Current.CancellationToken);

        var epics = await _service.GetAllAsync(Repo, TestContext.Current.CancellationToken);

        Assert.Equal("Mine", Assert.Single(epics).Title);
    }

    [Fact]
    public async Task Mediator_RejectsInvalidContractsBeforePersistenceOrGitHubCalls()
    {
        var mediator = _services.GetRequiredService<ISender>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new SaveEpicCommand(new Epic { RepositoryFullName = Repo, Title = new string('x', 257) }), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new AcceptEpicCommand(new("", 1)), TestContext.Current.CancellationToken));

        Assert.Empty((await mediator.Send(new GetEpicsQuery(new(Repo)), TestContext.Current.CancellationToken)).Dto.Items);
        Assert.Empty(_gitHub.Requests);
    }

    [Fact]
    public async Task Mediator_ReturnsDetachedContractsAndPreservesRepositoryIsolation()
    {
        var mediator = _services.GetRequiredService<ISender>();
        var saved = (await mediator.Send(new SaveEpicCommand(new Epic { RepositoryFullName = Repo, Title = "Original" }), TestContext.Current.CancellationToken)).Dto;
        saved.Title = "Not saved";

        var found = (await mediator.Send(new GetEpicQuery(new(Repo, saved.Id)), TestContext.Current.CancellationToken)).Dto;
        var otherRepository = (await mediator.Send(new GetEpicQuery(new("other/repo", saved.Id)), TestContext.Current.CancellationToken)).Dto;

        Assert.Equal("Original", found!.Title);
        Assert.Null(otherRepository);
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
        public bool CopilotAssignable { get; set; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new Request(request.Method.Method, path, body));

            return (request.Method.Method, path) switch
            {
                ("GET", "/repos/owner/repo/labels/epic") => new HttpResponseMessage(HttpStatusCode.NotFound),
                ("POST", "/repos/owner/repo/labels") => Json(HttpStatusCode.Created, """{"name":"epic"}"""),
                ("POST", "/repos/owner/repo/issues") => Json(HttpStatusCode.Created, Issue([])),
                ("POST", "/repos/owner/repo/issues/42/assignees") => Json(HttpStatusCode.Created,
                    Issue(CopilotAssignable ? ["""{"login":"Copilot"}"""] : [])),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static string Issue(string[] assignees)
        {
            return $$"""{"number":42,"title":"Epic","html_url":"https://github.com/owner/repo/issues/42","state":"open","assignees":[{{string.Join(",", assignees)}}]}""";
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
        {
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

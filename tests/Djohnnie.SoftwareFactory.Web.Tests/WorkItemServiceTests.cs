using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Djohnnie.SoftwareFactory.Web.Auth;
using MediatR;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests : IDisposable
{
    private const string Repo = "owner/repo";
    private readonly FakeGitHub _gitHub = new();
    private readonly WorkItemService _service;
    private readonly ServiceProvider _services;
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    public WorkItemServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var client = new GitHubApiClient(new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") }, new GitHubTokenProvider(new TokenAuth()));
        _services = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions))
            .AddSingleton(client).BuildServiceProvider();
        _service = _services.GetRequiredService<WorkItemService>();
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    [Fact]
    public void TypeRules_GiveEachTypeItsOwnLabelAndOnlyAssignStoriesBugsAndTasks()
    {
        Assert.Equal(["epic", "feature", "story", "bug", "task"], WorkItemTypeRules.All.Select(t => t.Label));
        Assert.Equal([WorkItemType.Story, WorkItemType.Bug, WorkItemType.Task], WorkItemTypeRules.All.Where(t => t.CanBeAssigned));
        Assert.Empty(WorkItemType.Epic.ParentTypes);
        Assert.Equal([WorkItemType.Epic], WorkItemType.Feature.ParentTypes);
        Assert.Equal([WorkItemType.Feature], WorkItemType.Story.ParentTypes);
        Assert.Equal([WorkItemType.Epic, WorkItemType.Feature, WorkItemType.Story], WorkItemType.Bug.ParentTypes);
        Assert.Equal([WorkItemType.Feature, WorkItemType.Bug, WorkItemType.Task], WorkItemType.Epic.ChildTypes);
        Assert.Empty(WorkItemType.Task.ChildTypes);
    }

    [Theory]
    [InlineData("owner/repo/../../user")]
    [InlineData("../user")]
    [InlineData("owner/..")]
    [InlineData("owner")]
    [InlineData("owner/repo?per_page=1")]
    public async Task InvalidRepositoryNames_AreRefusedBeforeGitHubOrTheDatabaseIsUsed(string name)
    {
        var gitHub = _services.GetRequiredService<GitHubService>();

        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.GetAllAsync(name, cancellationToken: _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.AcceptAsync(name, Guid.NewGuid(), _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => gitHub.GetFileAsync(name, "README.md", _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => gitHub.GetDirectoryAsync(name, "", _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => gitHub.PutFileAsync(name, "README.md", "content", "message", null, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => gitHub.GetCustomAgentsAsync(name, _cancellationToken));
        Assert.Empty(_gitHub.Requests);
    }

    [Fact]
    public async Task Accept_WhileAnotherAcceptIsGoingOn_IsRefusedWithoutCreatingAnIssue()
    {
        var bug = await SaveAsync(WorkItemType.Bug, "Crash");
        var store = _services.GetRequiredService<IWorkItemStore>();
        await store.BeginAcceptAsync(Repo, bug.Id, WorkItemWorkflow.AcceptClaimTimeout, _cancellationToken);

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.AcceptAsync(Repo, bug.Id, _cancellationToken));

        Assert.Contains("being accepted", error.Message);
        Assert.Empty(_gitHub.CreatedIssues);
    }

    [Fact]
    public async Task Accept_AfterAnAttemptThatMayHaveCreatedTheIssue_ReusesThatIssue()
    {
        var bug = await SaveAsync(WorkItemType.Bug, "Crash");
        _gitHub.NextIssueCreationStatus = HttpStatusCode.BadGateway;
        await Assert.ThrowsAsync<GitHubApiException>(() => _service.AcceptAsync(Repo, bug.Id, _cancellationToken));
        Assert.Equal(WorkItemStatus.Draft, (await _service.GetAsync(Repo, bug.Id, _cancellationToken))!.Status);

        var result = await _service.AcceptAsync(Repo, bug.Id, _cancellationToken);

        var issue = Assert.Single(_gitHub.CreatedIssues);
        Assert.Contains(WorkItemWorkflow.Marker(bug), issue.Body);
        Assert.Equal(issue.Number, result.WorkItem.IssueNumber);
        Assert.Equal(WorkItemStatus.InProgress, result.WorkItem.Status);
    }

    [Fact]
    public async Task Accept_AfterGitHubRefusedTheIssue_CreatesItWithoutLookingForAnEarlierOne()
    {
        var bug = await SaveAsync(WorkItemType.Bug, "Crash");
        _gitHub.NextIssueCreationStatus = HttpStatusCode.UnprocessableEntity;
        await Assert.ThrowsAsync<GitHubApiException>(() => _service.AcceptAsync(Repo, bug.Id, _cancellationToken));

        var result = await _service.AcceptAsync(Repo, bug.Id, _cancellationToken);

        Assert.Equal(42, Assert.Single(_gitHub.CreatedIssues).Number);
        Assert.Equal(42, result.WorkItem.IssueNumber);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Method == "GET" && r.Path == "/repos/owner/repo/issues");
    }

    [Fact]
    public async Task Accept_AnAcceptedItemAgain_IsRefusedWithoutCreatingAnotherIssue()
    {
        var bug = await SaveAsync(WorkItemType.Bug, "Crash");
        await _service.AcceptAsync(Repo, bug.Id, _cancellationToken);
        var store = _services.GetRequiredService<IWorkItemStore>();

        await Assert.ThrowsAsync<DomainRuleException>(() => store.BeginAcceptAsync(Repo, bug.Id, WorkItemWorkflow.AcceptClaimTimeout, _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => store.MarkAcceptedAsync(Repo, bug.Id, 1, 1, "https://example.com", _cancellationToken));
        Assert.Single(_gitHub.CreatedIssues);
    }

    [Fact]
    public async Task Accept_Hierarchy_CreatesLabelledSubIssuesAndOnlyAssignsTheStory()
    {
        var epic = await SaveAsync(WorkItemType.Epic, "Passkeys", customAgent: "ignored");
        Assert.Null(epic.CustomAgent);
        var feature = await SaveAsync(WorkItemType.Feature, "Passkey login", epic.Id);
        var story = await _service.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo,
            Type = WorkItemType.Story,
            ParentId = feature.Id,
            Title = " Sign in with a passkey ",
            Description = "As a user I want passkeys.",
            CustomAgent = "backend-dev",
            Model = "claude-sonnet-4.6",
            BaseBranch = ""
        }, _cancellationToken);

        var epicResult = await _service.AcceptAsync(Repo, epic.Id, _cancellationToken);
        var featureResult = await _service.AcceptAsync(Repo, feature.Id, _cancellationToken);
        var storyResult = await _service.AcceptAsync(Repo, story.Id, _cancellationToken);

        Assert.Equal(WorkItemStatus.Accepted, epicResult.WorkItem.Status);
        Assert.Equal(WorkItemStatus.Accepted, featureResult.WorkItem.Status);
        Assert.Equal(WorkItemStatus.InProgress, storyResult.WorkItem.Status);
        Assert.All([epicResult, featureResult, storyResult], r => Assert.Null(r.HierarchyError));
        Assert.Equal((42, 1042L, "https://github.com/owner/repo/issues/42"), (epicResult.WorkItem.IssueNumber!.Value, epicResult.WorkItem.IssueId!.Value, epicResult.WorkItem.IssueUrl!));

        var issues = _gitHub.Requests.Where(r => r is { Method: "POST", Path: "/repos/owner/repo/issues" })
            .Select(r => JsonDocument.Parse(r.Body!).RootElement).ToList();
        Assert.Equal(["epic", "feature", "story"], issues.Select(i => i.GetProperty("labels").EnumerateArray().Single().GetString()));
        Assert.Equal("Sign in with a passkey", issues[2].GetProperty("title").GetString());
        Assert.Contains(WorkItemWorkflow.Marker(story), issues[2].GetProperty("body").GetString());
        Assert.Equal(["epic", "feature", "story"], _gitHub.CreatedLabels);

        // The feature (#43, id 1043) is a sub-issue of the epic (#42), the story (#44, id 1044) of the feature.
        Assert.Equal([(42, 1043L), (43, 1044L)], _gitHub.SubIssues);

        var assign = Assert.Single(_gitHub.Requests, r => r.Path.EndsWith("/assignees"));
        Assert.Equal("/repos/owner/repo/issues/44/assignees", assign.Path);
        var body = JsonDocument.Parse(assign.Body!).RootElement;
        Assert.Equal("copilot-swe-agent[bot]", body.GetProperty("assignees")[0].GetString());
        var agent = body.GetProperty("agent_assignment");
        Assert.Equal(Repo, agent.GetProperty("target_repo").GetString());
        Assert.Equal("backend-dev", agent.GetProperty("custom_agent").GetString());
        Assert.Equal("claude-sonnet-4.6", agent.GetProperty("model").GetString());
        Assert.False(agent.TryGetProperty("base_branch", out _));
    }

    [Fact]
    public async Task Accept_BeforeTheParentIsAccepted_IsRefusedWithoutCallingGitHub()
    {
        var epic = await SaveAsync(WorkItemType.Epic, "Epic");
        var bug = await SaveAsync(WorkItemType.Bug, "Bug", epic.Id);

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.AcceptAsync(Repo, bug.Id, _cancellationToken));

        Assert.Equal("Accept the epic 'Epic' before accepting this bug.", error.Message);
        Assert.Empty(_gitHub.Requests);
        Assert.True((await _service.GetAsync(Repo, bug.Id, _cancellationToken))!.IsDraft);

        await _service.AcceptAsync(Repo, epic.Id, _cancellationToken);
        var bugResult = await _service.AcceptAsync(Repo, bug.Id, _cancellationToken);

        Assert.Null(bugResult.HierarchyError);
        Assert.Equal([(42, 1043L)], _gitHub.SubIssues);
    }

    [Fact]
    public async Task Accept_ParentAfterChildrenAcceptedBeforeTheRule_MakesThemSubIssues()
    {
        var epic = await SaveAsync(WorkItemType.Epic, "Epic");
        var bug = await SaveAsync(WorkItemType.Bug, "Bug", epic.Id);
        var draftTask = await SaveAsync(WorkItemType.Task, "Task", epic.Id);
        var store = _services.GetRequiredService<IWorkItemStore>();
        await store.BeginAcceptAsync(Repo, bug.Id, WorkItemWorkflow.AcceptClaimTimeout, _cancellationToken);
        await store.MarkAcceptedAsync(Repo, bug.Id, 1007, 7,"https://github.com/owner/repo/issues/7", _cancellationToken);

        await _service.AcceptAsync(Repo, epic.Id, _cancellationToken);

        Assert.Equal([(42, 1007L)], _gitHub.SubIssues);
        Assert.True((await _service.GetAsync(Repo, draftTask.Id, _cancellationToken))!.IsDraft);
    }

    [Fact]
    public async Task Accept_WhenLinkingTheSubIssueFails_KeepsTheIssueAndReportsIt()
    {
        var epic = await SaveAsync(WorkItemType.Epic, "Epic");
        await _service.AcceptAsync(Repo, epic.Id, _cancellationToken);
        var task = await SaveAsync(WorkItemType.Task, "Task", epic.Id);
        _gitHub.SubIssuesFail = true;

        var result = await _service.AcceptAsync(Repo, task.Id, _cancellationToken);

        Assert.Contains("Could not make #43 a sub-issue of #42", result.HierarchyError);
        Assert.Null(result.AssignmentError);
        Assert.Equal(WorkItemStatus.InProgress, result.WorkItem.Status);
    }

    [Fact]
    public async Task Accept_WhenCopilotIsNotAssigned_KeepsIssueAndAllowsRetry()
    {
        _gitHub.CopilotAssignable = false;
        var task = await SaveAsync(WorkItemType.Task, "Task");

        var result = await _service.AcceptAsync(Repo, task.Id, _cancellationToken);

        Assert.NotNull(result.AssignmentError);
        Assert.Equal(WorkItemStatus.Accepted, result.WorkItem.Status);
        Assert.Equal(42, result.WorkItem.IssueNumber);

        _gitHub.CopilotAssignable = true;
        var retry = await _service.AssignAgentAsync(Repo, task.Id, _cancellationToken);

        Assert.Null(retry.AssignmentError);
        Assert.Equal(WorkItemStatus.InProgress, retry.WorkItem.Status);
        Assert.Single(_gitHub.Requests, r => r is { Method: "POST", Path: "/repos/owner/repo/issues" });
    }

    [Fact]
    public async Task AssignAgent_ForEpicsAndFeatures_Throws()
    {
        var epic = await SaveAsync(WorkItemType.Epic, "Epic");
        await _service.AcceptAsync(Repo, epic.Id, _cancellationToken);

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.AssignAgentAsync(Repo, epic.Id, _cancellationToken));

        Assert.Contains("only user stories, bugs and tasks", error.Message);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path.EndsWith("/assignees"));
    }

    [Fact]
    public async Task Accept_WithoutDescription_Throws()
    {
        var bug = await _service.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Bug" }, _cancellationToken);

        await Assert.ThrowsAsync<DomainRuleException>(() => _service.AcceptAsync(Repo, bug.Id, _cancellationToken));
        Assert.Empty(_gitHub.Requests);
    }

    [Fact]
    public async Task Save_AfterAccept_OnlyChangesTheAssignmentOfAssignableItems()
    {
        _gitHub.CopilotAssignable = false;
        var epic = await SaveAsync(WorkItemType.Epic, "Epic");
        var bug = await SaveAsync(WorkItemType.Bug, "Bug");
        var accepted = (await _service.AcceptAsync(Repo, bug.Id, _cancellationToken)).WorkItem.Copy();

        accepted.Title = "Changed";
        accepted.ParentId = epic.Id;
        accepted.Model = "gpt-5";
        await _service.SaveAsync(accepted, _cancellationToken);

        var stored = await _service.GetAsync(Repo, bug.Id, _cancellationToken);
        Assert.Equal("Bug", stored!.Title);
        Assert.Null(stored.ParentId);
        Assert.Equal("gpt-5", stored.Model);

        var acceptedEpic = (await _service.AcceptAsync(Repo, epic.Id, _cancellationToken)).WorkItem;
        await Assert.ThrowsAsync<DomainRuleException>(() => _service.SaveAsync(acceptedEpic, _cancellationToken));
    }

    [Fact]
    public async Task Save_EnforcesTheHierarchy()
    {
        var epic = await SaveAsync(WorkItemType.Epic, "Epic");
        var feature = await SaveAsync(WorkItemType.Feature, "Feature", epic.Id);
        var story = await SaveAsync(WorkItemType.Story, "Story", feature.Id);
        var bug = await SaveAsync(WorkItemType.Bug, "Bug", story.Id);
        var otherEpic = await _service.SaveAsync(new WorkItem { RepositoryFullName = "other/repo", Type = WorkItemType.Epic, Title = "Other" }, _cancellationToken);

        Assert.Equal(new WorkItemReference(story.Id, WorkItemType.Story, "Story", WorkItemStatus.Draft, null, null), bug.Parent);
        Assert.Null((await SaveAsync(WorkItemType.Task, "Standalone task")).ParentId);

        var missingParent = await Assert.ThrowsAsync<ValidationFailedException>(() => SaveAsync(WorkItemType.Feature, "Orphan"));
        Assert.Equal("A feature needs a parent.", missingParent.Message);
        var wrongParent = await Assert.ThrowsAsync<DomainRuleException>(() => SaveAsync(WorkItemType.Story, "Story", epic.Id));
        Assert.Equal("A user story can only be part of a feature, not of an epic.", wrongParent.Message);
        await Assert.ThrowsAsync<DomainRuleException>(() => SaveAsync(WorkItemType.Task, "Task", bug.Id));
        await Assert.ThrowsAsync<DomainRuleException>(() => SaveAsync(WorkItemType.Epic, "Epic", epic.Id));
        await Assert.ThrowsAsync<DomainRuleException>(() => SaveAsync(WorkItemType.Feature, "Feature", otherEpic.Id));

        // The type of an existing item never changes.
        var changed = await _service.SaveAsync(bug with { Type = WorkItemType.Task }, _cancellationToken);
        Assert.Equal(WorkItemType.Bug, changed.Type);
    }

    [Fact]
    public async Task Delete_OnlyRemovesItemsWithoutChildren()
    {
        var epic = await SaveAsync(WorkItemType.Epic, "Epic");
        var feature = await SaveAsync(WorkItemType.Feature, "Feature", epic.Id);

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.DeleteAsync(Repo, epic.Id, _cancellationToken));
        Assert.Contains("still has 1 child item", error.Message);

        await _service.DeleteAsync(Repo, feature.Id, _cancellationToken);
        await _service.DeleteAsync(Repo, epic.Id, _cancellationToken);
        Assert.Empty(await _service.GetAllAsync(Repo, cancellationToken: _cancellationToken));
    }

    [Fact]
    public async Task GetAll_FiltersOnRepositoryTypeAndParent()
    {
        var epic = await SaveAsync(WorkItemType.Epic, "Mine");
        await SaveAsync(WorkItemType.Bug, "Child bug", epic.Id);
        await SaveAsync(WorkItemType.Bug, "Free bug");
        await _service.SaveAsync(new WorkItem { RepositoryFullName = "other/repo", Type = WorkItemType.Epic, Title = "Other" }, _cancellationToken);

        Assert.Equal("Mine", Assert.Single(await _service.GetAllAsync(Repo, [WorkItemType.Epic], cancellationToken: _cancellationToken)).Title);
        Assert.Equal(["Child bug", "Free bug"], (await _service.GetAllAsync(Repo, [WorkItemType.Bug], cancellationToken: _cancellationToken)).Select(i => i.Title).Order());
        Assert.Equal("Child bug", Assert.Single(await _service.GetAllAsync(Repo, parentId: epic.Id, cancellationToken: _cancellationToken)).Title);
        Assert.Equal(3, (await _service.GetAllAsync(Repo, cancellationToken: _cancellationToken)).Count);
    }

    [Fact]
    public async Task Mediator_RejectsInvalidContractsBeforePersistenceOrGitHubCalls()
    {
        var mediator = _services.GetRequiredService<ISender>();

        await Assert.ThrowsAsync<ValidationFailedException>(() => mediator.Send(new SaveWorkItemCommand(new WorkItem { RepositoryFullName = Repo, Title = new string('x', 257) }), _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => mediator.Send(new SaveWorkItemCommand(new WorkItem { RepositoryFullName = Repo, Type = (WorkItemType)42, Title = "Bad type" }), _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => mediator.Send(new AcceptWorkItemCommand(new("", Guid.NewGuid())), _cancellationToken));

        Assert.Empty((await mediator.Send(new GetWorkItemsQuery(new(Repo)), _cancellationToken)).Dto.Items);
        Assert.Empty(_gitHub.Requests);
    }

    [Fact]
    public async Task Mediator_ReturnsDetachedContractsAndPreservesRepositoryIsolation()
    {
        var mediator = _services.GetRequiredService<ISender>();
        var saved = (await mediator.Send(new SaveWorkItemCommand(new WorkItem { RepositoryFullName = Repo, Title = "Original" }), _cancellationToken)).Dto;
        saved.Title = "Not saved";

        var found = (await mediator.Send(new GetWorkItemQuery(new(Repo, saved.Id)), _cancellationToken)).Dto;
        var otherRepository = (await mediator.Send(new GetWorkItemQuery(new("other/repo", saved.Id)), _cancellationToken)).Dto;

        Assert.Equal("Original", found!.Title);
        Assert.Null(otherRepository);
    }

    private Task<WorkItem> SaveAsync(WorkItemType type, string title, Guid? parentId = null, string? customAgent = null)
    {
        return _service.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = type, ParentId = parentId, Title = title, Description = $"About {title}.", CustomAgent = customAgent
        }, _cancellationToken);
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

    private sealed record IssueOnGitHub(string Title, string? Body, string State, params string[] Assignees);

    /// <param name="State">GraphQL's OPEN, CLOSED or MERGED.</param>
    private sealed record PullRequestOnGitHub(int Number, string Title, string State, string Branch, string? ReviewDecision = null,
        string[]? LatestReviews = null, string? Checks = null);

    private sealed record RunOnGitHub(string Status, string? Conclusion, string Event = "dynamic", string Path = "dynamic/copilot-swe-agent/copilot");

    [GeneratedRegex(@"^/repos/owner/repo/issues/(\d+)(/\w+)?$")]
    private static partial Regex IssuePath();

    [GeneratedRegex(@"^/repos/owner/repo/pulls/(?<number>\d+)(?<rest>/\w+)?$")]
    private static partial Regex PullRequestPath();

    [GeneratedRegex(@"^/repos/owner/repo/issues/(?<number>\d+)/comments$")]
    private static partial Regex IssueCommentsPath();

    /// <summary>A GitHub fake: issues are numbered from 42 and get id 1000 + number; labels never exist yet.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private int _nextIssue = 42;

        public List<Request> Requests { get; } = [];
        public List<string> CreatedLabels { get; } = [];
        public List<(int Parent, long SubIssueId)> SubIssues { get; } = [];
        public bool CopilotAssignable { get; set; } = true;
        public bool SubIssuesFail { get; set; }
        public List<(int Number, string Body)> CreatedIssues { get; } = [];

        /// <summary>The issues as they were changed on GitHub, by number; a <c>null</c> value is an issue that was deleted.</summary>
        public Dictionary<int, IssueOnGitHub?> IssuesOnGitHub { get; } = [];

        /// <summary>The pull requests linked to an issue, by issue number; none when missing.</summary>
        public Dictionary<int, List<PullRequestOnGitHub>> LinkedPullRequests { get; } = [];

        /// <summary>The Actions runs of a branch, newest first; none when missing.</summary>
        public Dictionary<string, List<RunOnGitHub>> RunsByBranch { get; } = [];

        /// <summary>The state of each pull request by number: open, closed or merged; a missing one is open.</summary>
        public Dictionary<int, string> PullRequestStates { get; } = [];

        /// <summary>The files of every pull request.</summary>
        public List<(string FileName, string? Patch)> PullRequestFiles { get; } = [];

        /// <summary>The existing comments of every pull request's conversation, and on its lines.</summary>
        public List<string> ConversationComments { get; } = [];
        public List<(string Path, int Line, string Body)> LineComments { get; } = [];

        /// <summary>When set, submitting a review is refused with this status (e.g. 422 for a line no longer in the diff).</summary>
        public HttpStatusCode? ReviewFailure { get; set; }

        /// <summary>When set, GraphQL requests are refused with this status and message.</summary>
        public (HttpStatusCode Status, string Message)? GraphQlFailure { get; set; }

        /// <summary>When set, listing Actions runs is refused with this status (e.g. a token without Actions access).</summary>
        public HttpStatusCode? RunsFailure { get; set; }

        /// <summary>When set, every request that reads an issue is refused like this.</summary>
        public (HttpStatusCode Status, string Message)? IssueReadFailure { get; set; }

        /// <summary>
        /// The status of the next issue creation. A client error creates nothing; a server error still creates the issue,
        /// like a gateway error after GitHub created it.
        /// </summary>
        public HttpStatusCode? NextIssueCreationStatus { get; set; }

        private HttpResponseMessage CreateIssue(string body)
        {
            var status = NextIssueCreationStatus;
            NextIssueCreationStatus = null;
            if (status is { } refused && refused < HttpStatusCode.InternalServerError)
            {
                return Json(refused, """{"message":"Validation Failed"}""");
            }

            var number = _nextIssue++;
            CreatedIssues.Add((number, JsonDocument.Parse(body).RootElement.GetProperty("body").GetString()!));
            return status is { } error ? Json(error, """{"message":"Bad gateway"}""") : Json(HttpStatusCode.Created, Issue(number, []));
        }

        private static string IssueWithBody(int number, string body)
        {
            return $$"""{"id":{{1000 + number}},"number":{{number}},"title":"Issue","html_url":"https://github.com/owner/repo/issues/{{number}}","state":"open","body":{{JsonSerializer.Serialize(body)}},"assignees":[]}""";
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method.Method;
            Requests.Add(new Request(method, path, body));

            if (method == "GET" && path.StartsWith("/repos/owner/repo/labels/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (method == "POST" && path == "/repos/owner/repo/labels")
            {
                CreatedLabels.Add(JsonDocument.Parse(body!).RootElement.GetProperty("name").GetString()!);
                return Json(HttpStatusCode.Created, body!);
            }
            if (method == "POST" && path == "/repos/owner/repo/issues")
            {
                return CreateIssue(body!);
            }
            if (method == "POST" && path == "/graphql")
            {
                return GraphQlFailure is { } refused
                    ? Json(refused.Status, JsonSerializer.Serialize(new { message = refused.Message }))
                    : LinkedPullRequestsResponse(body!);
            }
            if (method == "GET" && path == "/repos/owner/repo/actions/runs")
            {
                return RunsResponse(request.RequestUri!);
            }
            if (PullRequestPath().Match(path) is { Success: true } pull)
            {
                return PullRequestResponse(method, int.Parse(pull.Groups["number"].Value), pull.Groups["rest"].Value);
            }
            if (IssueCommentsPath().Match(path) is { Success: true } comments)
            {
                var commented = int.Parse(comments.Groups["number"].Value);
                return method == "POST"
                    ? Json(HttpStatusCode.Created, $$"""{"html_url":"https://github.com/owner/repo/pull/{{commented}}#issuecomment-1"}""")
                    : Json(HttpStatusCode.OK, JsonSerializer.Serialize(ConversationComments.Select((text, index) => new
                    {
                        id = index + 1, user = new { login = "reviewer" }, body = text,
                        created_at = DateTimeOffset.UnixEpoch.AddHours(index), html_url = $"https://github.com/owner/repo/pull/{commented}#issuecomment-{index + 1}"
                    })));
            }
            if (method == "GET" && path == "/repos/owner/repo/issues")
            {
                var issues = CreatedIssues.AsEnumerable().Reverse().Select(i => IssueWithBody(i.Number, i.Body));
                return Json(HttpStatusCode.OK, $"[{string.Join(",", issues)}]");
            }

            var match = IssuePath().Match(path);
            if (!match.Success)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            var number = int.Parse(match.Groups[1].Value);
            return (method, match.Groups[2].Value) switch
            {
                ("GET", "") when IssueReadFailure is { } failure => Json(failure.Status, JsonSerializer.Serialize(new { message = failure.Message })),
                ("GET", "") when IssuesOnGitHub.TryGetValue(number, out var changed) => changed is null
                    ? Json(HttpStatusCode.NotFound, """{"message":"Not Found"}""")
                    : Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
                    {
                        id = 1000 + number,
                        number,
                        title = changed.Title,
                        html_url = $"https://github.com/owner/repo/issues/{number}",
                        state = changed.State,
                        body = changed.Body,
                        assignees = changed.Assignees.Select(login => new { login })
                    })),
                ("GET", "") => Json(HttpStatusCode.OK, Issue(number, [])),
                ("POST", "/assignees") => Json(HttpStatusCode.Created, Issue(number, CopilotAssignable ? ["""{"login":"Copilot"}"""] : [])),
                ("POST", "/sub_issues") when SubIssuesFail => Json(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed"}"""),
                ("POST", "/sub_issues") => AddSubIssue(number, body!),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private HttpResponseMessage PullRequestResponse(string method, int number, string rest)
        {
            var state = PullRequestStates.GetValueOrDefault(number, "open");
            return (method, rest) switch
            {
                ("GET", "") => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    number, title = "Fix the task", html_url = $"https://github.com/owner/repo/pull/{number}",
                    state = state == "merged" ? "closed" : state, draft = false, user = new { login = "Copilot" },
                    merged_at = state == "merged" ? DateTimeOffset.UnixEpoch : (DateTimeOffset?)null,
                    additions = 3, deletions = 1, changed_files = PullRequestFiles.Count
                })),
                ("GET", "/files") => Json(HttpStatusCode.OK, JsonSerializer.Serialize(PullRequestFiles.Select(f => new
                {
                    filename = f.FileName, status = "modified", additions = 1, deletions = 1, patch = f.Patch,
                    blob_url = $"https://github.com/owner/repo/blob/main/{f.FileName}"
                }))),
                ("GET", "/comments") => Json(HttpStatusCode.OK, JsonSerializer.Serialize(LineComments.Select((c, index) => new
                {
                    id = 100 + index, user = new { login = "reviewer" }, body = c.Body, path = c.Path, line = c.Line,
                    created_at = DateTimeOffset.UnixEpoch.AddMinutes(30 + index), html_url = $"https://github.com/owner/repo/pull/{number}#discussion_r{100 + index}"
                }))),
                ("POST", "/reviews") when ReviewFailure is { } failure => Json(failure, """{"message":"Unprocessable Entity","errors":["Line could not be resolved"]}"""),
                ("POST", "/reviews") => Json(HttpStatusCode.OK, $$"""{"id":9,"html_url":"https://github.com/owner/repo/pull/{{number}}#pullrequestreview-9"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private HttpResponseMessage LinkedPullRequestsResponse(string body)
        {
            var number = JsonDocument.Parse(body).RootElement.GetProperty("variables").GetProperty("number").GetInt32();
            var nodes = LinkedPullRequests.GetValueOrDefault(number, []).Select((pr, index) => new
            {
                number = pr.Number,
                url = $"https://github.com/owner/repo/pull/{pr.Number}",
                title = pr.Title,
                state = pr.State,
                isDraft = pr.State == "OPEN",
                headRefName = pr.Branch,
                updatedAt = DateTimeOffset.UnixEpoch.AddDays(index),
                reviewDecision = pr.ReviewDecision,
                latestReviews = new { nodes = (pr.LatestReviews ?? []).Select(state => new { state }) },
                commits = new { nodes = new[] { new { commit = new { statusCheckRollup = pr.Checks is null ? null : new { state = pr.Checks } } } } }
            });
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                data = new { repository = new { issue = new { closedByPullRequestsReferences = new { nodes } } } }
            }));
        }

        private HttpResponseMessage RunsResponse(Uri uri)
        {
            if (RunsFailure is { } failure)
            {
                return Json(failure, """{"message":"Resource not accessible by integration"}""");
            }

            var branch = System.Web.HttpUtility.ParseQueryString(uri.Query)["branch"] ?? string.Empty;
            var runs = RunsByBranch.GetValueOrDefault(branch, []).Select((run, index) => new
            {
                id = 500 + index,
                name = "Copilot",
                @event = run.Event,
                path = run.Path,
                status = run.Status,
                conclusion = run.Conclusion,
                html_url = $"https://github.com/owner/repo/actions/runs/{500 + index}",
                created_at = DateTimeOffset.UnixEpoch.AddDays(-index)
            });
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { workflow_runs = runs }));
        }

        private HttpResponseMessage AddSubIssue(int parent, string body)
        {
            SubIssues.Add((parent, JsonDocument.Parse(body).RootElement.GetProperty("sub_issue_id").GetInt64()));
            return Json(HttpStatusCode.Created, Issue(parent, []));
        }

        private static string Issue(int number, string[] assignees)
        {
            return $$"""{"id":{{1000 + number}},"number":{{number}},"title":"Issue","html_url":"https://github.com/owner/repo/issues/{{number}}","state":"open","assignees":[{{string.Join(",", assignees)}}]}""";
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
        {
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

using System.Net;
using System.Text;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Djohnnie.SoftwareFactory.Contracts.Assistant;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class GitHubApiClientTests
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetRepositories_FollowsNextLinksWhoseUrlsContainCommas()
    {
        const string page2 = "https://api.github.com/user/repos?sort=pushed&affiliation=owner,collaborator,organization_member&per_page=100&page=2";
        var handler = new Handler(request => request.RequestUri!.Query.Contains("page=2")
            ? Json($"[{Repository("b")}]")
            : Json($"[{Repository("a")}]", $"<{page2}>; rel=\"next\", <{page2}>; rel=\"last\""));

        var repositories = await Client(handler).GetRepositoriesAsync(_cancellationToken);

        Assert.Equal(["owner/a", "owner/b"], repositories.Select(r => r.FullName).Order());
        Assert.Equal(page2, handler.Requests[1].AbsoluteUri);
    }

    [Fact]
    public async Task GetRepositories_StopsWithoutANextLink()
    {
        var handler = new Handler(_ => Json($"[{Repository("a")}]", "<https://api.github.com/user/repos?page=1>; rel=\"first\", <https://api.github.com/user/repos?page=1>; rel=\"prev\""));

        var repositories = await Client(handler).GetRepositoriesAsync(_cancellationToken);

        Assert.Single(repositories);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetFile_DecodesBase64Content()
    {
        var handler = new Handler(_ => Json($$"""{"path":"README.md","sha":"abc","type":"file","encoding":"base64","content":"{{Convert.ToBase64String("Hello"u8.ToArray())}}\n"}"""));

        var file = await Client(handler).GetFileAsync("owner/repo", "README.md", _cancellationToken);

        Assert.Equal("Hello", file!.Content);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetFile_OverOneMegabyte_FetchesTheRawContent()
    {
        var handler = new Handler(request => request.Headers.Accept.ToString().Contains("raw")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Large content") }
            : Json("""{"path":"big.json","sha":"abc","type":"file","encoding":"none","content":""}"""));

        var file = await Client(handler).GetFileAsync("owner/repo", "big.json", _cancellationToken);

        Assert.Equal("Large content", file!.Content);
        Assert.Equal("application/vnd.github.raw+json", handler.AcceptHeaders[1]);
    }

    [Theory]
    [InlineData("""[{"name":"a.md","path":"docs/a.md","sha":"1","type":"file"}]""", "is a directory")]
    [InlineData("""{"path":"docs","sha":"1","type":"submodule"}""", "is a submodule")]
    [InlineData("""{"path":"link","sha":"1","type":"symlink","target":"README.md"}""", "is a symlink")]
    public async Task GetFile_ForSomethingElseThanAFile_ExplainsWhat(string response, string expected)
    {
        var handler = new Handler(_ => Json(response));

        var error = await Assert.ThrowsAsync<GitHubApiException>(() => Client(handler).GetFileAsync("owner/repo", "docs", _cancellationToken));

        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("../../../user")]
    [InlineData("docs/../../../other/private/contents/secret.txt")]
    [InlineData("./README.md")]
    public async Task FileRequests_RefuseDotSegments_WithoutCallingGitHub(string path)
    {
        var handler = new Handler(_ => throw new InvalidOperationException("GitHub must not be called."));
        var client = Client(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetFileAsync("owner/repo", path, _cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetDirectoryAsync("owner/repo", path, _cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.PutFileAsync("owner/repo", path, "content", "message", null, _cancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("../../../user")]
    [InlineData("src/../../other-repo")]
    [InlineData("..\\..\\user")]
    public async Task CopilotInstructionsTools_RefusePathsOutsideTheRepository(string path)
    {
        var requested = new List<string>();
        var tools = new CopilotInstructionsFormTools(
            () => throw new InvalidOperationException("The form is not read."),
            _ => { },
            (directory, _) =>
            {
                requested.Add(directory);
                return Task.FromResult<IReadOnlyList<RepositoryEntry>>([]);
            },
            (file, _) =>
            {
                requested.Add(file);
                return Task.FromResult<string?>("secret");
            },
            change =>
            {
                change();
                return Task.CompletedTask;
            });

        var read = await tools.ReadRepositoryFileAsync(path, _cancellationToken);
        var listed = await tools.ListRepositoryFilesAsync(path, _cancellationToken);

        Assert.Contains("can't contain '..'", read);
        Assert.Contains("can't contain '..'", listed);
        Assert.Empty(requested);
    }

    [Fact]
    public async Task Unauthorized_ReportsTheRejectedTokenToEndTheSession()
    {
        var token = new Token();
        var client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)), token);

        var error = await Assert.ThrowsAsync<GitHubApiException>(() => client.GetRepositoryAsync("owner/a", _cancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(1, token.UnauthorizedReports);
    }

    [Fact]
    public async Task OtherErrors_DoNotEndTheSession()
    {
        var token = new Token();
        var client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)), token);

        await Assert.ThrowsAsync<GitHubApiException>(() => client.GetRepositoryAsync("owner/a", _cancellationToken));

        Assert.Equal(0, token.UnauthorizedReports);
    }

    [Fact]
    public async Task Get_RetriesATransientFailure()
    {
        var calls = 0;
        var handler = new Handler(_ => ++calls == 1 ? Failure(HttpStatusCode.ServiceUnavailable, retryAfterSeconds: 0) : Json(Repository("a")));

        var repository = await ResilientClient(handler).GetRepositoryAsync("owner/a", _cancellationToken);

        Assert.Equal("owner/a", repository.FullName);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Get_GivesUpAfterTheLastRetry()
    {
        var handler = new Handler(_ => Failure(HttpStatusCode.BadGateway, retryAfterSeconds: 0));

        var error = await Assert.ThrowsAsync<GitHubApiException>(() => ResilientClient(handler).GetRepositoryAsync("owner/a", _cancellationToken));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Equal(1 + GitHubApiResilience.MaxRetries, handler.Requests.Count);
    }

    [Fact]
    public async Task Post_IsNeverRetried()
    {
        var handler = new Handler(_ => Failure(HttpStatusCode.ServiceUnavailable, retryAfterSeconds: 0));

        await Assert.ThrowsAsync<GitHubApiException>(() => ResilientClient(handler).CreateIssueCommentAsync("owner/a", 1, "Hello", _cancellationToken));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GraphQlQuery_IsRetriedAlthoughItIsAPost()
    {
        var handler = new Handler(_ => Failure(HttpStatusCode.ServiceUnavailable, retryAfterSeconds: 0));

        await Assert.ThrowsAsync<GitHubApiException>(() => ResilientClient(handler).GetLinkedPullRequestsAsync("owner/a", 1, _cancellationToken));

        Assert.Equal(1 + GitHubApiResilience.MaxRetries, handler.Requests.Count);
    }

    [Fact]
    public async Task SecondaryRateLimit_WithAShortWait_IsRetried()
    {
        var calls = 0;
        var handler = new Handler(_ => ++calls == 1 ? Failure(HttpStatusCode.Forbidden, retryAfterSeconds: 0) : Json(Repository("a")));

        await ResilientClient(handler).GetRepositoryAsync("owner/a", _cancellationToken);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PrimaryRateLimit_ThatResetsLater_FailsAtOnce()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        var handler = new Handler(_ =>
        {
            var response = Failure(HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", reset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return response;
        });

        var error = await Assert.ThrowsAsync<GitHubApiException>(() => ResilientClient(handler).GetRepositoryAsync("owner/a", _cancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Forbidden_WithoutRateLimitHeaders_IsNotRetried()
    {
        var handler = new Handler(_ => Failure(HttpStatusCode.Forbidden));

        await Assert.ThrowsAsync<GitHubApiException>(() => ResilientClient(handler).GetRepositoryAsync("owner/a", _cancellationToken));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public void RequestedWait_ReadsRetryAfterAndTheRateLimitReset()
    {
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var retryAfter = Failure(HttpStatusCode.TooManyRequests, retryAfterSeconds: 42);
        var reset = Failure(HttpStatusCode.Forbidden);
        reset.Headers.Add("x-ratelimit-remaining", "0");
        reset.Headers.Add("x-ratelimit-reset", now.AddSeconds(10).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(TimeSpan.FromSeconds(42), GitHubApiResilience.RequestedWait(retryAfter, now));
        Assert.Equal(TimeSpan.FromSeconds(11), GitHubApiResilience.RequestedWait(reset, now));
        Assert.Null(GitHubApiResilience.RequestedWait(Failure(HttpStatusCode.ServiceUnavailable), now));
    }

    private static GitHubApiClient Client(Handler handler, Token? token = null)
    {
        return new GitHubApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") }, token ?? new Token());
    }

    /// <summary>The client as the application registers it, with its resilience pipeline, on <paramref name="handler"/>.</summary>
    private static GitHubApiClient ResilientClient(Handler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGitHubTokenProvider>(new Token());
        services.AddGitHubApiClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<GitHubApiClient>();
    }

    private static HttpResponseMessage Failure(HttpStatusCode status, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("""{"message":"failed"}""", Encoding.UTF8, "application/json") };
        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }
        return response;
    }

    private static string Repository(string name)
    {
        return $$"""{"id":1,"name":"{{name}}","full_name":"owner/{{name}}","owner":{"login":"owner"},"html_url":"https://github.com/owner/{{name}}","private":false,"fork":false,"archived":false}""";
    }

    private static HttpResponseMessage Json(string json, string? link = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (link is not null)
        {
            response.Headers.Add("Link", link);
        }
        return response;
    }

    private sealed class Token : IGitHubTokenProvider
    {
        public int UnauthorizedReports { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult("token");
        }

        public void ReportUnauthorized()
        {
            UnauthorizedReports++;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string> AcceptHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            AcceptHeaders.Add(request.Headers.Accept.ToString());
            return Task.FromResult(respond(request));
        }
    }
}

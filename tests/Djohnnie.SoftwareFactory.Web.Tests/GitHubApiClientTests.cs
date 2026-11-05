using System.Net;
using System.Text;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
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

    private static GitHubApiClient Client(Handler handler)
    {
        return new GitHubApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") }, new Token());
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
        public Task<string> GetAccessTokenAsync()
        {
            return Task.FromResult("token");
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}

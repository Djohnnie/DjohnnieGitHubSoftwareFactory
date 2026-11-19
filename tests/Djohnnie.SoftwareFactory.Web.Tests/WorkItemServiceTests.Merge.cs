using System.Net;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    [Theory]
    [InlineData(PullRequestMergeMethod.Merge, "merge")]
    [InlineData(PullRequestMergeMethod.Squash, "squash")]
    [InlineData(PullRequestMergeMethod.Rebase, "rebase")]
    public async Task Merge_MergesThePullRequestWithTheChosenMethod(PullRequestMergeMethod method, string expected)
    {
        var task = await TaskWithPullRequestAsync();

        var result = await _service.MergePullRequestAsync(new(Repo, task.Id, method), _cancellationToken);

        Assert.Equal("https://github.com/owner/repo/pull/7", result.Url);
        Assert.Equal("abc123", result.Sha);
        var merged = Assert.Single(_gitHub.Requests, r => r.Method == "PUT" && r.Path == "/repos/owner/repo/pulls/7/merge");
        Assert.Equal(expected, JsonDocument.Parse(merged.Body!).RootElement.GetProperty("merge_method").GetString());
    }

    [Theory]
    [InlineData("merged")]
    [InlineData("closed")]
    public async Task Merge_OfAClosedPullRequest_IsRefused(string state)
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.PullRequestStates[7] = state;

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.MergePullRequestAsync(new(Repo, task.Id), _cancellationToken));
        Assert.Contains(state, error.Message);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Method == "PUT");
    }

    [Fact]
    public async Task Merge_OfADraft_IsRefused()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.PullRequestStates[7] = "draft";

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.MergePullRequestAsync(new(Repo, task.Id), _cancellationToken));
        Assert.Contains("draft", error.Message);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Method == "PUT");
    }

    [Theory]
    [InlineData(HttpStatusCode.MethodNotAllowed, "Required status check \"build\" is expected.")]
    [InlineData(HttpStatusCode.Conflict, "branch changed in the meantime")]
    public async Task Merge_GitHubRefuses_IsReported(HttpStatusCode status, string expected)
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.MergeFailure = (status, "Required status check \"build\" is expected.");

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.MergePullRequestAsync(new(Repo, task.Id), _cancellationToken));
        Assert.StartsWith("GitHub didn't merge pull request #7", error.Message);
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task Merge_WithoutAPullRequest_IsRefused()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.MergePullRequestAsync(new(Repo, task.Id), _cancellationToken));
        Assert.Contains("no pull request yet", error.Message);
    }

    [Fact]
    public async Task Merge_WithAnUnknownMethod_IsInvalid()
    {
        var task = await TaskWithPullRequestAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.MergePullRequestAsync(new(Repo, task.Id, (PullRequestMergeMethod)42), _cancellationToken));
    }
}

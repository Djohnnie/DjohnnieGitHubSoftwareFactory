using System.Net;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    [Fact]
    public async Task Approval_IsAnApprovingReviewThatDoesNotMentionCopilot()
    {
        var task = await TaskWithPullRequestAsync();

        var result = await _service.ApprovePullRequestAsync(new(Repo, task.Id, "  Ship it.  "), _cancellationToken);

        Assert.Equal("https://github.com/owner/repo/pull/7#pullrequestreview-9", result.Url);
        var posted = Assert.Single(_gitHub.Requests, r => r.Method == "POST" && r.Path == "/repos/owner/repo/pulls/7/reviews");
        var review = JsonDocument.Parse(posted.Body!).RootElement;
        Assert.Equal("APPROVE", review.GetProperty("event").GetString());
        Assert.Equal("Ship it.", review.GetProperty("body").GetString());
        Assert.Empty(review.GetProperty("comments").EnumerateArray());
    }

    [Fact]
    public async Task Approval_WithoutAComment_HasAnEmptyBody()
    {
        var task = await TaskWithPullRequestAsync();

        await _service.ApprovePullRequestAsync(new(Repo, task.Id), _cancellationToken);

        var review = JsonDocument.Parse(_gitHub.Requests.Single(r => r.Method == "POST" && r.Path.EndsWith("/reviews")).Body!).RootElement;
        Assert.Equal(string.Empty, review.GetProperty("body").GetString());
    }

    [Theory]
    [InlineData("merged")]
    [InlineData("closed")]
    public async Task Approval_OfAClosedPullRequest_IsRefused(string state)
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.PullRequestStates[7] = state;

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.ApprovePullRequestAsync(new(Repo, task.Id), _cancellationToken));
        Assert.Contains(state, error.Message);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Method == "POST" && r.Path.EndsWith("/reviews"));
    }

    [Fact]
    public async Task Approval_GitHubRefuses_IsReported()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.ReviewFailure = HttpStatusCode.UnprocessableEntity;

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.ApprovePullRequestAsync(new(Repo, task.Id), _cancellationToken));
        Assert.StartsWith("GitHub didn't accept the approval", error.Message);
    }

    [Fact]
    public async Task Approval_WithoutAPullRequest_IsRefused()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.ApprovePullRequestAsync(new(Repo, task.Id), _cancellationToken));
        Assert.Contains("no pull request yet", error.Message);
    }

    [Fact]
    public async Task PullRequestChanges_IncludeTheReviewStatusAndMergeState()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.Reviews.Add(("copilot-pull-request-reviewer[bot]", "COMMENTED"));
        _gitHub.Reviews.Add(("djohnnie", "APPROVED"));
        _gitHub.MergeableState = "clean";

        var changes = await _service.GetPullRequestChangesAsync(Repo, task.Id, _cancellationToken);

        Assert.Equal(PullRequestReviewDecision.Approved, changes.Review.Decision);
        Assert.Equal("clean", changes.Review.MergeState);
        Assert.Equal([("djohnnie", "APPROVED"), ("copilot-pull-request-reviewer[bot]", "COMMENTED")],
            changes.Review.Reviewers.Select(r => (r.Login, r.State)));
        Assert.Equal("https://github.com/owner/repo/pull/7#pullrequestreview-201", changes.Review.Reviewers[0].Url);
    }

    [Fact]
    public void ReviewStatus_AReviewerIsJudgedOnTheirLatestVerdict_NotOnLaterComments()
    {
        var status = WorkItemPullRequests.ReviewStatus([
            Review("alice", "CHANGES_REQUESTED", 1),
            Review("alice", "APPROVED", 2),
            Review("alice", "COMMENTED", 3),
            Review("bob", "PENDING", 4)
        ], "blocked");

        var reviewer = Assert.Single(status.Reviewers);
        Assert.Equal(("alice", "APPROVED"), (reviewer.Login, reviewer.State));
        Assert.Equal(PullRequestReviewDecision.Approved, status.Decision);
    }

    [Fact]
    public void ReviewStatus_ChangesRequestedByAnyone_WinsOverAnApproval()
    {
        var status = WorkItemPullRequests.ReviewStatus([Review("alice", "APPROVED", 1), Review("bob", "CHANGES_REQUESTED", 2)], null);

        Assert.Equal(PullRequestReviewDecision.ChangesRequested, status.Decision);
    }

    [Fact]
    public void ReviewStatus_ADismissedApproval_NoLongerCounts()
    {
        var status = WorkItemPullRequests.ReviewStatus([Review("alice", "APPROVED", 1), Review("alice", "DISMISSED", 2)], null);

        Assert.Equal(PullRequestReviewDecision.None, status.Decision);
        Assert.Equal("DISMISSED", Assert.Single(status.Reviewers).State);
    }

    [Fact]
    public async Task Approval_WithATooLongComment_IsInvalid()
    {
        var task = await TaskWithPullRequestAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.ApprovePullRequestAsync(
            new(Repo, task.Id, new string('x', PullRequestApproval.MaxBodyLength + 1)), _cancellationToken));
    }

    private static GitHubPullRequestReview Review(string login, string state, int hour)
    {
        return new(new GitHubOwner(login, null), DateTimeOffset.UnixEpoch.AddHours(hour), state);
    }
}

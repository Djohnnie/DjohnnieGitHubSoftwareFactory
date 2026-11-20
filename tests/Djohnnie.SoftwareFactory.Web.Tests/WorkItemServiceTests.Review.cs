using System.Text.Json;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    [Fact]
    public void ErrorDetail_ReadsStringAndObjectErrors()
    {
        var error = JsonSerializer.Deserialize<Djohnnie.SoftwareFactory.Contracts.GitHub.Api.ErrorResponse>(
            """{"message":"Validation Failed","errors":["Can not approve your own pull request",{"resource":"PullRequestReview","field":"body","code":"missing"},{"message":"Too long"}]}""");

        Assert.Equal("Validation Failed (Can not approve your own pull request; body missing; Too long)",
            Djohnnie.SoftwareFactory.Application.Features.GitHub.GitHubApiClient.ErrorDetail(error));
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
    public void ReviewStatus_ARequestedCopilotReview_IsAwaitedUntilGitHubRemovesTheRequest()
    {
        var reviews = new[] { Review("alice", "APPROVED", 1) };

        Assert.True(WorkItemPullRequests.ReviewStatus(reviews, "clean",
            [new GitHubOwner("bob", null, "User"), new GitHubOwner("Copilot", null, "Bot")]).CopilotReviewRequested);
        Assert.False(WorkItemPullRequests.ReviewStatus(reviews, "clean", [new GitHubOwner("bob", null, "User")]).CopilotReviewRequested);
        Assert.False(WorkItemPullRequests.ReviewStatus(reviews, "clean").CopilotReviewRequested);
    }

    private static GitHubPullRequestReview Review(string login, string state, int hour)
    {
        return new(new GitHubOwner(login, null), DateTimeOffset.UnixEpoch.AddHours(hour), state);
    }
}

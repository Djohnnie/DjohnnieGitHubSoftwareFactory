using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class CopilotProgressEvaluatorTests
{
    private static GitHubLinkedPullRequest PullRequest(string state = "OPEN", string title = "Fix it", string? reviewDecision = null,
        string[]? latestReviews = null, string? checks = null, int number = 7, int updatedDay = 0)
    {
        return new(number, $"https://github.com/o/r/pull/{number}", title, state, state == "OPEN", "copilot/fix", DateTimeOffset.UnixEpoch.AddDays(updatedDay),
            reviewDecision, latestReviews ?? [], checks);
    }

    private static GitHubWorkflowRun Run(string status, string? conclusion = null)
    {
        return new(1, "Copilot", "dynamic", "dynamic/copilot-swe-agent/copilot", status, conclusion, "https://github.com/o/r/actions/runs/1", DateTimeOffset.UnixEpoch);
    }

    [Theory]
    [InlineData(false, true, CopilotProgress.Queued)]
    [InlineData(false, false, CopilotProgress.None)]
    [InlineData(true, true, CopilotProgress.None)]
    public void WithoutPullRequest_DependsOnTheAssignment(bool closed, bool assigned, CopilotProgress expected)
    {
        Assert.Equal(expected, CopilotProgressEvaluator.Evaluate(closed, assigned, null, null).Progress);
    }

    [Theory]
    [InlineData("queued", null, CopilotProgress.Working)]
    [InlineData("in_progress", null, CopilotProgress.Working)]
    [InlineData("completed", "failure", CopilotProgress.Stopped)]
    [InlineData("completed", "cancelled", CopilotProgress.Stopped)]
    [InlineData("completed", "timed_out", CopilotProgress.Stopped)]
    [InlineData("completed", "success", CopilotProgress.ReadyForReview)]
    public void TheRunDecidesWhetherCopilotIsBusy(string status, string? conclusion, CopilotProgress expected)
    {
        Assert.Equal(expected, CopilotProgressEvaluator.Evaluate(false, true, PullRequest(title: "[WIP] Fix it"), Run(status, conclusion)).Progress);
    }

    [Fact]
    public void ARunningSession_WinsOverAReviewThatAskedForChanges()
    {
        var pullRequest = PullRequest(reviewDecision: "CHANGES_REQUESTED");

        Assert.Equal(CopilotProgress.Working, CopilotProgressEvaluator.Evaluate(false, true, pullRequest, Run("in_progress")).Progress);
        Assert.Equal(CopilotProgress.ChangesRequested, CopilotProgressEvaluator.Evaluate(false, true, pullRequest, Run("completed", "success")).Progress);
    }

    [Theory]
    [InlineData("APPROVED", new string[0], CopilotProgress.Approved)]
    [InlineData("REVIEW_REQUIRED", new[] { "APPROVED" }, CopilotProgress.ReadyForReview)]
    [InlineData(null, new[] { "COMMENTED", "APPROVED" }, CopilotProgress.Approved)]
    [InlineData(null, new[] { "APPROVED", "CHANGES_REQUESTED" }, CopilotProgress.ChangesRequested)]
    [InlineData(null, new string[0], CopilotProgress.ReadyForReview)]
    public void Reviews_UseTheDecisionOrElseTheLatestReviews(string? decision, string[] latestReviews, CopilotProgress expected)
    {
        var pullRequest = PullRequest(reviewDecision: decision, latestReviews: latestReviews);

        Assert.Equal(expected, CopilotProgressEvaluator.Evaluate(false, true, pullRequest, null).Progress);
    }

    [Fact]
    public void WithoutTheRun_TheWorkInProgressTitleMeansWorking()
    {
        Assert.Equal(CopilotProgress.Working, CopilotProgressEvaluator.Evaluate(false, true, PullRequest(title: "[WIP] Fix it"), null).Progress);
    }

    [Fact]
    public void SelectPullRequest_PrefersOpenThenMergedThenNewest()
    {
        var closed = PullRequest("CLOSED", number: 1, updatedDay: 3);
        var merged = PullRequest("MERGED", number: 2, updatedDay: 1);
        var open = PullRequest("OPEN", number: 3, updatedDay: 0);

        Assert.Equal(3, CopilotProgressEvaluator.SelectPullRequest([closed, merged, open])!.Number);
        Assert.Equal(2, CopilotProgressEvaluator.SelectPullRequest([closed, merged])!.Number);
        Assert.Equal(1, CopilotProgressEvaluator.SelectPullRequest([closed, PullRequest("CLOSED", number: 4, updatedDay: 2)])!.Number);
        Assert.Null(CopilotProgressEvaluator.SelectPullRequest([]));
    }

    [Theory]
    [InlineData("SUCCESS", PullRequestChecks.Passing)]
    [InlineData("FAILURE", PullRequestChecks.Failing)]
    [InlineData("ERROR", PullRequestChecks.Failing)]
    [InlineData("PENDING", PullRequestChecks.Pending)]
    [InlineData("EXPECTED", PullRequestChecks.Pending)]
    [InlineData(null, PullRequestChecks.None)]
    public void Checks_FollowTheRollup(string? state, PullRequestChecks expected)
    {
        Assert.Equal(expected, CopilotProgressEvaluator.ChecksOf(state));
    }
}

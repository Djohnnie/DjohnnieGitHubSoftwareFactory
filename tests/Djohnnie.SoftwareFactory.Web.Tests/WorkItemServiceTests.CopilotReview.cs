using System.Net;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>The sync asking Copilot code review to review a revision the Copilot cloud agent made on feedback from DGHSF.</summary>
public partial class WorkItemServiceTests
{
    private const string CopilotBranch = "copilot/fix-42";

    /// <summary>Copilot's pull request #7 of task #42, with feedback sent to Copilot from DGHSF.</summary>
    private async Task<WorkItem> TaskWithFeedbackForCopilotAsync()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "success", CreatedAt: DateTimeOffset.UtcNow.AddHours(-1))];
        await _service.SendPullRequestFeedbackAsync(new(Repo, task.Id, "Please add tests.", []), _cancellationToken);
        return (await _service.GetAsync(Repo, task.Id, _cancellationToken))!;
    }

    [Fact]
    public async Task FeedbackForCopilot_IsRemembered_UntilCopilotFinished_ThenTheSyncRequestsOneCopilotReview()
    {
        var before = DateTimeOffset.UtcNow;
        var task = await TaskWithFeedbackForCopilotAsync();
        Assert.True(task.CopilotRevisionRequestedAt >= before);

        // Copilot's session from before the feedback doesn't count.
        await Sync.SyncAcceptedAsync(_cancellationToken);
        Assert.Empty(_gitHub.CopilotReviewRequests);

        _gitHub.RunsByBranch[CopilotBranch] = [new("in_progress", null, CreatedAt: DateTimeOffset.UtcNow.AddMinutes(1))];
        await Sync.SyncAcceptedAsync(_cancellationToken);
        Assert.Empty(_gitHub.CopilotReviewRequests);

        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "success", CreatedAt: DateTimeOffset.UtcNow.AddMinutes(1))];
        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal([7], _gitHub.CopilotReviewRequests);
        var request = Assert.Single(_gitHub.Requests, r => r.Path == "/repos/owner/repo/pulls/7/requested_reviewers");
        Assert.Equal("copilot-pull-request-reviewer[bot]", JsonDocument.Parse(request.Body!).RootElement.GetProperty("reviewers")[0].GetString());
        Assert.Null((await _service.GetAsync(Repo, task.Id, _cancellationToken))!.CopilotRevisionRequestedAt);
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRequested);
        Assert.Equal((SyncLogLevel.Information, task.Id), (logged.Level, logged.WorkItemId));
        Assert.Contains("pull request #7", logged.Message);
    }

    [Fact]
    public async Task CopilotReview_IsNotRequested_WhenThePullRequestWasMergedMeanwhile()
    {
        var task = await TaskWithFeedbackForCopilotAsync();
        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "MERGED", CopilotBranch)];

        await Sync.SyncAcceptedAsync(_cancellationToken);

        await AssertReviewAbandonedAsync(task, "pull request #7 is merged.");
    }

    [Fact]
    public async Task CopilotReview_IsNotRequested_WhenCopilotsSessionFailed()
    {
        var task = await TaskWithFeedbackForCopilotAsync();
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "failure", CreatedAt: DateTimeOffset.UtcNow.AddMinutes(1))];

        await Sync.SyncAcceptedAsync(_cancellationToken);

        await AssertReviewAbandonedAsync(task, "Copilot's agent session ended with 'failure'");
    }

    [Fact]
    public async Task CopilotReview_IsNoLongerAwaited_WhenCopilotNeverStartedOnTheFeedback()
    {
        var task = await TaskWithFeedbackForCopilotAsync();
        _gitHub.RunsByBranch.Remove(CopilotBranch);
        await _services.GetRequiredService<IWorkItemStore>().SetCopilotRevisionRequestedAsync(Repo, task.Id,
            DateTimeOffset.UtcNow - CopilotRevisionReviews.StartTimeout - TimeSpan.FromMinutes(1), _cancellationToken);

        await Sync.SyncAcceptedAsync(_cancellationToken);

        await AssertReviewAbandonedAsync(task, "Copilot didn't start working on the feedback within 6 hours.");
    }

    [Fact]
    public async Task CopilotReview_IsNotRequested_WithoutAccessToActions()
    {
        var task = await TaskWithFeedbackForCopilotAsync();
        _gitHub.RunsFailure = HttpStatusCode.Forbidden;

        await Sync.SyncAcceptedAsync(_cancellationToken);

        await AssertReviewAbandonedAsync(task, "GitHub refused to list the Actions runs");
    }

    [Fact]
    public async Task CopilotReview_ThatGitHubRefuses_IsReportedOnce()
    {
        var task = await TaskWithFeedbackForCopilotAsync();
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "success", CreatedAt: DateTimeOffset.UtcNow.AddMinutes(1))];
        _gitHub.ReviewRequestFailure = (HttpStatusCode.UnprocessableEntity, "Reviews may only be requested from collaborators.");

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(result.Failures);
        Assert.Single(_gitHub.Requests, r => r.Path == "/repos/owner/repo/pulls/7/requested_reviewers");
        await AssertReviewAbandonedAsync(task, "GitHub refused to request the review: ");
        Assert.Contains("Reviews may only be requested from collaborators.", Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewAbandoned).Message);
    }

    private async Task AssertReviewAbandonedAsync(WorkItem task, string reason)
    {
        Assert.Empty(_gitHub.CopilotReviewRequests);
        Assert.Null((await _service.GetAsync(Repo, task.Id, _cancellationToken))!.CopilotRevisionRequestedAt);
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewAbandoned);
        Assert.Equal((SyncLogLevel.Warning, task.Id), (logged.Level, logged.WorkItemId));
        Assert.Contains(reason, logged.Message);
    }
}

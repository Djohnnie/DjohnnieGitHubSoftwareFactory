using System.Net;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>The sync handing a pull request of the Copilot cloud agent over for review once one of its agent sessions finished.</summary>
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

    [Fact]
    public async Task CopilotsFirstSession_ThatFinished_MarksTheDraftReadyForReview_AndRequestsOneCopilotReview()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.PullRequestStates[7] = "draft";
        _gitHub.RunsByBranch[CopilotBranch] = [new("in_progress", null, CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5))];

        // While Copilot works, nothing happens.
        await Sync.SyncAcceptedAsync(_cancellationToken);
        Assert.Empty(_gitHub.MarkedReadyForReview);
        Assert.Empty(_gitHub.CopilotReviewRequests);

        var session = DateTimeOffset.UtcNow.AddMinutes(-5);
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "success", CreatedAt: session)];
        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(["PR_7"], _gitHub.MarkedReadyForReview);
        Assert.Equal([7], _gitHub.CopilotReviewRequests);
        var stored = (await _service.GetAsync(Repo, task.Id, _cancellationToken))!;
        Assert.Equal(session.ToUnixTimeSeconds(), stored.CopilotSessionHandledAt?.ToUnixTimeSeconds());
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRequested);
        Assert.Equal((SyncLogLevel.Information, task.Id), (logged.Level, logged.WorkItemId));
        Assert.Contains("Copilot finished its agent session on pull request #7", logged.Message);
        Assert.Contains("it was marked ready for review and Copilot code review was asked to review it.", logged.Message);

        // A next session of Copilot (e.g. after an @copilot comment on GitHub) is handed over again.
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "success", CreatedAt: DateTimeOffset.UtcNow.AddMinutes(1))];
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal([7, 7], _gitHub.CopilotReviewRequests);
        Assert.Single(_gitHub.MarkedReadyForReview);
    }

    [Fact]
    public async Task CopilotsSession_ThatFailed_IsNotHandedOverForReview()
    {
        await TaskWithPullRequestAsync();
        _gitHub.PullRequestStates[7] = "draft";
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "failure", CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5))];

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(_gitHub.MarkedReadyForReview);
        Assert.Empty(_gitHub.CopilotReviewRequests);
        Assert.DoesNotContain(await SyncLogAsync(), e => e.Kind is SyncLogEventKind.CopilotReviewRequested or SyncLogEventKind.CopilotReviewAbandoned);
    }

    [Fact]
    public async Task CopilotsSession_ThatCopilotCodeReviewReviewedAlready_OnlyMarksTheDraftReadyForReview()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.PullRequestStates[7] = "draft";
        var session = DateTimeOffset.UtcNow.AddMinutes(-5);
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "success", CreatedAt: session)];
        _gitHub.ReviewsOnGitHub.Add(new(new("copilot-pull-request-reviewer[bot]", null, "Bot"), session.AddMinutes(2), "COMMENTED", Id: 300));

        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(["PR_7"], _gitHub.MarkedReadyForReview);
        Assert.Empty(_gitHub.CopilotReviewRequests);
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.PullRequestMarkedReadyForReview);
        Assert.Equal((SyncLogLevel.Information, task.Id), (logged.Level, logged.WorkItemId));
        Assert.Contains("so it was marked ready for review; Copilot code review was asked to review it already.", logged.Message);
    }

    [Fact]
    public async Task CopilotsSession_OnAPullRequestThatIsNoDraft_OnlyRequestsTheReview()
    {
        await TaskWithPullRequestAsync();
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "success", CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5))];

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(_gitHub.MarkedReadyForReview);
        Assert.Equal([7], _gitHub.CopilotReviewRequests);
        Assert.DoesNotContain("marked ready", Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRequested).Message);
    }

    [Fact]
    public async Task CopilotsSession_WhoseDraftGitHubWontMarkReady_StillGetsItsReview_WithAWarning()
    {
        await TaskWithPullRequestAsync();
        _gitHub.PullRequestStates[7] = "draft";
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "success", CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5))];
        _gitHub.MarkReadyFailure = ("FORBIDDEN", "Resource not accessible by integration");

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal([7], _gitHub.CopilotReviewRequests);
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRequested);
        Assert.Equal(SyncLogLevel.Warning, logged.Level);
        Assert.Contains("The draft couldn't be marked ready for review: ", logged.Message);
        Assert.Contains("Resource not accessible by integration", logged.Message);
    }

    [Fact]
    public async Task FeedbackForCopilot_MarksTheDraftReadyForReview_BeforeTheReview_AndIsHandledOnce()
    {
        await TaskWithFeedbackForCopilotAsync();
        _gitHub.PullRequestStates[7] = "draft";
        _gitHub.RunsByBranch[CopilotBranch] = [new("completed", "success", CreatedAt: DateTimeOffset.UtcNow.AddMinutes(1))];

        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(["PR_7"], _gitHub.MarkedReadyForReview);
        Assert.Equal([7], _gitHub.CopilotReviewRequests);
        var markedAt = _gitHub.Requests.FindIndex(r => r.Path == "/graphql" && r.Body!.Contains("markPullRequestReadyForReview", StringComparison.Ordinal));
        var requestedAt = _gitHub.Requests.FindIndex(r => r.Path == "/repos/owner/repo/pulls/7/requested_reviewers");
        Assert.True(markedAt < requestedAt);
        Assert.Contains("Copilot finished the revision of pull request #7", Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRequested).Message);
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

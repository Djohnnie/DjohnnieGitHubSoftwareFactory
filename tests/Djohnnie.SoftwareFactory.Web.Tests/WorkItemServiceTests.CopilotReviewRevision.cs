using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>The sync asking the item's agent to revise once Copilot code review left remarks on its pull request.</summary>
public partial class WorkItemServiceTests
{
    private static readonly GitHubOwner CopilotReviewer = new(GitHubApiClient.CopilotReviewerLogin, null, "Bot");

    private long _nextReviewId = 500;

    /// <summary>Worker task #42 that revises on Copilot code review, whose run opened pull request #7 and ended an hour ago.</summary>
    private async Task<WorkItem> WorkerTaskRevisingOnCopilotReviewAsync(bool revise = true)
    {
        var saved = await _service.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Task", Description = "About Task.",
            Executor = AgentExecutor.DghsfWorker, ReviseOnCopilotReview = revise
        }, _cancellationToken);
        Assert.Equal(revise, saved.ReviseOnCopilotReview);
        var task = (await _service.AcceptAsync(Repo, saved.Id, _cancellationToken)).WorkItem;

        var run = Assert.Single(await _services.GetRequiredService<AgentRunService>().GetRunsAsync(Repo, task.Id, _cancellationToken));
        await SetRunAsync(run.Id, AgentRunStatus.Succeeded, pullRequest: 7);
        var factory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.AgentRuns.SingleAsync(r => r.Id == run.Id, _cancellationToken);
            entity.CompletedAt = DateTimeOffset.UtcNow.AddHours(-1);
            await db.SaveChangesAsync(_cancellationToken);
        }
        return await RefreshWorkerTaskAsync(task);
    }

    /// <summary>Adds a review of Copilot code review, submitted that long ago, with that many line comments.</summary>
    private void CopilotReviews(TimeSpan ago, int remarks, string state = "COMMENTED")
    {
        var id = _nextReviewId++;
        var submittedAt = DateTimeOffset.UtcNow - ago;
        _gitHub.ReviewsOnGitHub.Add(new(CopilotReviewer, submittedAt, state, $"https://github.com/owner/repo/pull/7#pullrequestreview-{id}", id,
            "## Pull request overview"));
        for (var index = 0; index < remarks; index++)
        {
            _gitHub.ThreadedLineComments.Add(new(id * 10 + index, CopilotReviewer, $"Remark {index + 1}.", submittedAt,
                $"https://github.com/owner/repo/pull/7#discussion_r{id * 10 + index}", "src/App.cs", 3, PullRequestReviewId: id));
        }
    }

    private async Task<AgentRun> TaskRunAsync(WorkItem task)
    {
        return Assert.Single(await _services.GetRequiredService<AgentRunService>().GetRunsAsync(Repo, task.Id, _cancellationToken));
    }

    /// <summary>Copilot task #42 that revises on Copilot code review, with Copilot's pull request #7 ready for review.</summary>
    private async Task<WorkItem> CopilotTaskRevisingOnCopilotReviewAsync()
    {
        var saved = await _service.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Task", Description = "About Task.",
            Executor = AgentExecutor.CopilotCloudAgent, ReviseOnCopilotReview = true
        }, _cancellationToken);
        Assert.True(saved.ReviseOnCopilotReview);
        var task = (await _service.AcceptAsync(Repo, saved.Id, _cancellationToken)).WorkItem;
        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", CopilotBranch)];
        var refreshed = await RefreshAssignedTaskAsync(task);
        Assert.Equal(7, refreshed.PullRequestNumber);
        return refreshed;
    }

    [Fact]
    public async Task Sync_AsksCopilotToAddressTheRemarks_WhenCopilotCodeReviewLeftRemarks()
    {
        var task = await CopilotTaskRevisingOnCopilotReviewAsync();
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 2);

        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        var posted = Assert.Single(CommentsPostedOnPullRequest7());
        Assert.StartsWith($"@copilot {CopilotReviewRevisions.CopilotRequest}", posted);
        Assert.Contains("- `src/App.cs` line 3 (https://github.com/owner/repo/pull/7#discussion_r", posted);
        Assert.Contains("  > Remark 1.", posted);
        Assert.Contains("  > Remark 2.", posted);
        var stored = (await _service.GetAsync(Repo, task.Id, _cancellationToken))!;
        Assert.NotNull(stored.CopilotReviewHandledAt);
        // Remembered as feedback, so the sync asks Copilot code review to review Copilot's revision.
        Assert.NotNull(stored.CopilotRevisionRequestedAt);
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRevisionStarted);
        Assert.Equal((SyncLogLevel.Information, task.Id), (logged.Level, logged.WorkItemId));
        Assert.Contains("left 2 remarks on pull request #7", logged.Message);
        Assert.Contains("so Copilot was asked to revise it", logged.Message);
    }

    [Fact]
    public async Task Sync_DoesntAskCopilotAgain_WhileItsRevisionIsPending()
    {
        await CopilotTaskRevisingOnCopilotReviewAsync();
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 1);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        CopilotReviews(TimeSpan.FromMinutes(5), remarks: 1);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Single(CommentsPostedOnPullRequest7());
    }

    [Fact]
    public async Task Sync_DoesntAskCopilot_WhenCopilotReviewedWithoutRemarks()
    {
        var task = await CopilotTaskRevisingOnCopilotReviewAsync();
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 0);

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(CommentsPostedOnPullRequest7());
        Assert.NotNull((await _service.GetAsync(Repo, task.Id, _cancellationToken))!.CopilotReviewHandledAt);
    }

    [Fact]
    public async Task Sync_DoesntAskCopilot_WhileCopilotIsWorking()
    {
        await CopilotTaskRevisingOnCopilotReviewAsync();
        _gitHub.LinkedPullRequests[42] = [new(7, "[WIP] Task", "OPEN", CopilotBranch)];
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 1);

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(CommentsPostedOnPullRequest7());
    }

    [Fact]
    public async Task Sync_StopsAskingCopilotAutomatically_AfterTheMaximumNumberOfRounds()
    {
        var task = await CopilotTaskRevisingOnCopilotReviewAsync();
        for (var round = 0; round < CopilotReviewRevisions.MaxRounds; round++)
        {
            CopilotReviews(TimeSpan.FromHours(5 - round), remarks: 1);
        }
        await using (var db = await _services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(w => w.Id == task.Id, _cancellationToken);
            entity.CopilotReviewHandledAt = DateTimeOffset.UtcNow.AddHours(-3);
            await db.SaveChangesAsync(_cancellationToken);
        }
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 1);

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(CommentsPostedOnPullRequest7());
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRevisionSkipped);
        Assert.Contains($"round {CopilotReviewRevisions.MaxRounds + 1}", logged.Message);
        Assert.Contains("Copilot wasn't asked to revise it", logged.Message);
    }

    [Fact]
    public void CopilotFeedback_ListsARequestForChangesWithoutLineComments_ByItsText()
    {
        var review = new GitHubPullRequestReview(CopilotReviewer, DateTimeOffset.UtcNow, "CHANGES_REQUESTED", "https://github.com/owner/repo/pull/7#pullrequestreview-1", 1,
            "Handle null.\nAnd empty.");

        var feedback = CopilotReviewRevisions.CopilotFeedback([review], []);

        Assert.Equal($"{CopilotReviewRevisions.CopilotRequest}\n\n- The review https://github.com/owner/repo/pull/7#pullrequestreview-1 requested changes:\n  > Handle null.\n  > And empty.",
            feedback.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Sync_StartsARevisionOfTheWorker_WhenCopilotCodeReviewLeftRemarks()
    {
        var task = await WorkerTaskRevisingOnCopilotReviewAsync();
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 2);

        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        var run = await TaskRunAsync(task);
        Assert.Equal((AgentRunKind.Revise, AgentRunStatus.Pending, 7), (run.Kind, run.Status, run.PullRequestNumber));
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRevisionStarted);
        Assert.Equal((SyncLogLevel.Information, task.Id), (logged.Level, logged.WorkItemId));
        Assert.Contains("left 2 remarks on pull request #7", logged.Message);
        Assert.NotNull((await _service.GetAsync(Repo, task.Id, _cancellationToken))!.CopilotReviewHandledAt);
    }

    [Fact]
    public async Task Sync_StartsNoRevision_WhenTheItemDoesntAskForIt()
    {
        var task = await WorkerTaskRevisingOnCopilotReviewAsync(revise: false);
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 2);

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(AgentRunStatus.Succeeded, (await TaskRunAsync(task)).Status);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path == "/repos/owner/repo/pulls/7/reviews");
    }

    [Fact]
    public async Task Sync_StartsNoRevision_WhenCopilotReviewedWithoutRemarks()
    {
        var task = await WorkerTaskRevisingOnCopilotReviewAsync();
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 0);

        await Sync.SyncAcceptedAsync(_cancellationToken);
        _gitHub.Requests.Clear();
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(AgentRunStatus.Succeeded, (await TaskRunAsync(task)).Status);
        Assert.NotNull((await _service.GetAsync(Repo, task.Id, _cancellationToken))!.CopilotReviewHandledAt);
        // The review was handled, so the next sync doesn't read its comments again.
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path == "/repos/owner/repo/pulls/7/comments");
        Assert.DoesNotContain(await SyncLogAsync(), e => e.Kind is SyncLogEventKind.CopilotReviewRevisionStarted or SyncLogEventKind.CopilotReviewRevisionSkipped);
    }

    [Fact]
    public async Task Sync_StartsNoRevision_ForAReviewFromBeforeTheWorkersLastRunEnded()
    {
        var task = await WorkerTaskRevisingOnCopilotReviewAsync();
        CopilotReviews(TimeSpan.FromHours(2), remarks: 2);

        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(AgentRunStatus.Succeeded, (await TaskRunAsync(task)).Status);
    }

    [Fact]
    public async Task Sync_StopsRevisingAutomatically_AfterTheMaximumNumberOfRounds()
    {
        var task = await WorkerTaskRevisingOnCopilotReviewAsync();
        for (var round = 0; round < CopilotReviewRevisions.MaxRounds; round++)
        {
            CopilotReviews(TimeSpan.FromHours(5 - round), remarks: 1);
        }
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 1);

        await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Equal(AgentRunStatus.Succeeded, (await TaskRunAsync(task)).Status);
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRevisionSkipped);
        Assert.Equal((SyncLogLevel.Warning, task.Id), (logged.Level, logged.WorkItemId));
        Assert.Contains($"round {CopilotReviewRevisions.MaxRounds + 1}", logged.Message);
    }

    [Fact]
    public async Task Sync_ReportsOnce_WhenTheRevisionIsRefused()
    {
        var task = await WorkerTaskRevisingOnCopilotReviewAsync();
        CopilotReviews(TimeSpan.FromMinutes(10), remarks: 1);
        _gitHub.PullRequestStates[7] = "closed";

        var result = await Sync.SyncAcceptedAsync(_cancellationToken);
        await Sync.SyncAcceptedAsync(_cancellationToken);

        Assert.Empty(result.Failures);
        Assert.Equal(AgentRunStatus.Succeeded, (await TaskRunAsync(task)).Status);
        var logged = Assert.Single(await SyncLogAsync(), e => e.Kind == SyncLogEventKind.CopilotReviewRevisionSkipped);
        Assert.Contains("Pull request #7 is closed", logged.Message);
    }
}

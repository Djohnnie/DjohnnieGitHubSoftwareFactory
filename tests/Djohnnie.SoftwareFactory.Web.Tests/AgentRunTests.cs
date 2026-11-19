using System.Net;
using System.Text;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Djohnnie.SoftwareFactory.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>
/// The DGHSF agent worker's runs against a real SQL Server: the store's conditional updates (claims, leases) keep duplicate
/// Service Bus messages and crashed job executions harmless, and the executor records what each run cost.
/// </summary>
public class AgentRunTests
{
    private const string Repo = "owner/repo";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);
    private static readonly AgentRunUsage Usage = new(2_500_000_000, true, 1.5m, 1200, 300, 800, 100, "claude-sonnet-4.5");
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARun_IsClaimedOnce_StartedOnce_AndStoresItsPullRequestAndCost()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new AgentRunStore(database.Factory, TimeProvider.System);
        var workItem = Guid.NewGuid();
        var run = await store.CreateAsync("Owner/Repo", workItem, "claude-sonnet-4.5", 2, _cancellationToken);
        await Assert.ThrowsAsync<DomainRuleException>(() => store.CreateAsync(Repo, workItem, null, 2, _cancellationToken));

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => store.ClaimDueAsync(10, _cancellationToken))));
        Assert.Equal([run.Id], claims.SelectMany(ids => ids));
        var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => store.TryStartAsync(run.Id, Lease, _cancellationToken))));
        var started = Assert.Single(starts, s => s is not null)!;
        Assert.Equal((AgentRunStatus.Running, 1), (started.Status, started.Attempts));
        Assert.True(await store.RenewLeaseAsync(run.Id, Lease, _cancellationToken));

        Assert.True(await store.CompleteAsync(run.Id, new AgentRunResult("dghsf/42-task-1a2b3c4d", 7, "https://github.com/owner/repo/pull/7", "Did it.", Usage), _cancellationToken));

        var done = (await store.GetLatestAsync(Repo, workItem, _cancellationToken))!;
        Assert.Equal((AgentRunStatus.Succeeded, 7, "dghsf/42-task-1a2b3c4d", "Did it."), (done.Status, done.PullRequestNumber, done.Branch, done.Summary));
        Assert.Equal(2.5m, done.AiCredits);
        Assert.Equal((1200L, 300L, 800L, 100L, 1.5m), (done.InputTokens, done.OutputTokens, done.CacheReadTokens, done.CacheWriteTokens, done.PremiumRequests));
        Assert.True(done.UsageComplete);
        Assert.NotNull(done.CompletedAt);
        Assert.False(await store.RenewLeaseAsync(run.Id, Lease, _cancellationToken));

        // Another run on the item reopens its task with more attempts; it keeps the pull request until it opens a new one.
        var again = await store.CreateAsync(Repo, workItem, null, 2, _cancellationToken);
        Assert.Equal((run.Id, AgentRunStatus.Pending, 1, 3, 7, (DateTimeOffset?)null), (again.Id, again.Status, again.Attempts, again.MaxAttempts, again.PullRequestNumber, again.CompletedAt));
    }

    [Fact]
    public async Task Migration_FoldsTheTasksOfAWorkItemIntoItsFirstTask_LeavingItemsWithATaskGoingOn()
    {
        await using var database = await SqlServerDatabase.CreateAsync("20261119123437_AddRepositoryArchived");
        Guid opened = Guid.NewGuid(), failedRevision = Guid.NewGuid(), revision = Guid.NewGuid(), busyFirst = Guid.NewGuid(), busy = Guid.NewGuid();
        Guid item = Guid.NewGuid(), busyItem = Guid.NewGuid();
        await using (var old = database.Factory.CreateDbContext())
        {
            // Inserted one by one, so SysId follows the order the tasks were added in.
            foreach (var (id, workItem, status, kind, attempts, nanoAiu, feedbackUntil, summary) in new (Guid, Guid, string, string, int, long, string?, string?)[]
            {
                (opened, item, "Succeeded", "Implement", 2, 1_000_000_000, "2026-11-19T10:00:00+00:00", "Opened it."),
                (failedRevision, item, "Failed", "Revise", 1, 500_000_000, null, null),
                (revision, item, "Succeeded", "Revise", 1, 200_000_000, "2026-11-19T12:00:00+00:00", "Revised it."),
                (busyFirst, busyItem, "Succeeded", "Implement", 1, 0, null, null),
                (busy, busyItem, "Running", "Revise", 1, 0, null, null)
            })
            {
                await old.Database.ExecuteSqlRawAsync("""
                    INSERT INTO [AgentRuns] ([Id], [WorkItemId], [RepositoryFullName], [Status], [Kind], [Attempts], [MaxAttempts], [NanoAiu], [UsageComplete],
                        [InputTokens], [OutputTokens], [CacheReadTokens], [CacheWriteTokens], [PullRequestNumber], [Branch], [FeedbackUntil], [Summary], [Error],
                        [CreatedAt], [CompletedAt])
                    VALUES ({0}, {1}, 'owner/repo', {2}, {3}, {4}, 2, {5}, 1, 10, 5, 0, 0, 7, 'dghsf/42', CAST(NULLIF({6}, '') AS datetimeoffset), NULLIF({7}, ''),
                        CASE WHEN {2} = 'Failed' THEN 'No new feedback.' END, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                    """, [id, workItem, status, kind, attempts, nanoAiu, feedbackUntil ?? string.Empty, summary ?? string.Empty],
                    _cancellationToken);
                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    await old.Database.ExecuteSqlRawAsync("""
                        INSERT INTO [AgentRunAttempts] ([Id], [AgentRunId], [Attempt], [Status], [UsageComplete], [InputTokens], [OutputTokens], [CacheReadTokens],
                            [CacheWriteTokens], [StartedAt])
                        VALUES (NEWID(), {0}, {1}, {2}, 1, 0, 0, 0, 0, SYSDATETIMEOFFSET());
                        """, [id, attempt, attempt < attempts ? "Failed" : status], _cancellationToken);
                }
            }
            await old.Database.ExecuteSqlRawAsync("""
                INSERT INTO [AgentRunModelUsages] ([Id], [AgentRunId], [Attempt], [Model], [NanoAiu], [Complete], [Requests], [PremiumRequests], [InputTokens],
                    [OutputTokens], [CacheReadTokens], [CacheWriteTokens], [ReasoningTokens], [OccurredAt])
                VALUES (NEWID(), {0}, 1, 'claude-sonnet-4.5', 200000000, 1, 1, 1, 10, 5, 0, 0, 0, SYSDATETIMEOFFSET());
                """, [revision], _cancellationToken);
        }

        await using var db = database.Factory.CreateDbContext();
        await db.Database.MigrateAsync(_cancellationToken);

        var store = new AgentRunStore(database.Factory, TimeProvider.System);
        var task = Assert.Single(await store.GetForWorkItemAsync(Repo, item, _cancellationToken));
        Assert.Equal((opened, AgentRunStatus.Succeeded, AgentRunKind.Revise, 4, 1.7m, 30L, "Revised it.", (string?)null),
            (task.Id, task.Status, task.Kind, task.Attempts, task.AiCredits, task.InputTokens, task.Summary, task.Error));
        Assert.Equal(DateTimeOffset.Parse("2026-11-19T12:00:00+00:00"), task.FeedbackUntil);
        var overview = (await store.GetForRepositoryAsync(Repo, 10, _cancellationToken)).Single(t => t.Run.Id == opened);
        Assert.Equal(
            [(4, AgentRunKind.Revise), (3, AgentRunKind.Revise), (2, AgentRunKind.Implement), (1, AgentRunKind.Implement)],
            overview.Runs.Select(r => (r.Attempt, r.Kind)));
        var usage = await db.AgentRunModelUsages.SingleAsync(_cancellationToken);
        Assert.Equal((opened, 4), (usage.AgentRunId, usage.Attempt));

        // An item whose follow-up run is still going on keeps its tasks, so the worker's run isn't moved under it.
        Assert.Equal([busy, busyFirst], (await store.GetForWorkItemAsync(Repo, busyItem, _cancellationToken)).Select(r => r.Id));
    }

    [Fact]
    public async Task EveryRunOnAWorkItem_RetriesRevisionsAndFreshStarts_IsARunOfItsOneTask()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new AgentRunStore(database.Factory, TimeProvider.System);
        var run = await StartedAsync(store, maxAttempts: 2);
        Assert.Equal(AgentRunStatus.Pending, await store.FailAsync(run.Id, "git clone failed (128).", null, retry: true, _cancellationToken));
        await store.ClaimDueAsync(10, _cancellationToken);
        await store.TryStartAsync(run.Id, Lease, _cancellationToken);
        var opened = new AgentRunResult("dghsf/42-task-1a2b3c4d", 7, "https://github.com/owner/repo/pull/7", "Did it.", Usage);
        Assert.True(await store.CompleteAsync(run.Id, opened, _cancellationToken));
        var handled = (await store.GetAsync(run.Id, _cancellationToken))!.FeedbackUntil;

        // A revision on the pull request's feedback is the task's third run, which works on the pull request from the start.
        var revision = await store.CreateRevisionAsync(Repo, run.WorkItemId, "gpt-5", 1, "dghsf/42-task-1a2b3c4d", 7, "https://github.com/owner/repo/pull/7", _cancellationToken);
        Assert.Equal((run.Id, AgentRunKind.Revise, AgentRunStatus.Pending, 3, "gpt-5"), (revision.Id, revision.Kind, revision.Status, revision.MaxAttempts, revision.RequestedModel));
        await Assert.ThrowsAsync<DomainRuleException>(() => store.CreateRevisionAsync(Repo, run.WorkItemId, null, 1, "b", 7, "u", _cancellationToken));
        await store.ClaimDueAsync(10, _cancellationToken);
        Assert.Equal(3, (await store.TryStartAsync(run.Id, Lease, _cancellationToken))!.Attempts);
        Assert.Equal(AgentRunStatus.Failed, await store.FailAsync(run.Id, "No new feedback.", null, retry: false, _cancellationToken));

        // A failed revision keeps the pull request and how far its discussion was handled.
        var failed = (await store.GetAsync(run.Id, _cancellationToken))!;
        Assert.Equal((AgentRunStatus.Failed, 7, handled, "Did it."), (failed.Status, failed.PullRequestNumber, failed.FeedbackUntil, failed.Summary));
        Assert.Equal(handled, PullRequestDiscussion.HandledUntil([failed], 7));

        // A fresh start is a run of the same task too; a cancelled one leaves the task with its pull request.
        var fresh = await store.CreateAsync(Repo, run.WorkItemId, null, 1, _cancellationToken);
        Assert.Equal((run.Id, AgentRunKind.Implement, 7), (fresh.Id, fresh.Kind, fresh.PullRequestNumber));
        await store.ClaimDueAsync(10, _cancellationToken);
        await store.TryStartAsync(run.Id, Lease, _cancellationToken);
        Assert.True(await store.CancelAsync(Repo, run.Id, _cancellationToken));
        Assert.Equal(run.Id, (await store.GetLatestAsync(Repo, run.WorkItemId, _cancellationToken))?.Id);

        var task = Assert.Single(await store.GetForRepositoryAsync(Repo, 10, _cancellationToken));
        Assert.Equal(4, task.Run.Attempts);
        Assert.Equal(
            [
                (4, AgentRunKind.Implement, AgentRunStatus.Cancelled, (int?)null),
                (3, AgentRunKind.Revise, AgentRunStatus.Failed, 7),
                (2, AgentRunKind.Implement, AgentRunStatus.Succeeded, 7),
                (1, AgentRunKind.Implement, AgentRunStatus.Failed, null)
            ],
            task.Runs.Select(r => (r.Attempt, r.Kind, r.Status, r.PullRequestNumber)));
    }

    [Fact]
    public async Task Fail_WithRetry_QueuesTheRunAgainWhileItHasAttempts_AndAddsUpWhatEveryAttemptCost()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new AgentRunStore(database.Factory, TimeProvider.System);
        var run = await StartedAsync(store, maxAttempts: 2);

        Assert.Equal(AgentRunStatus.Pending, await store.FailAsync(run.Id, "git clone failed (128).", null, retry: true, _cancellationToken));
        await store.ClaimDueAsync(10, _cancellationToken);
        Assert.Equal(2, (await store.TryStartAsync(run.Id, Lease, _cancellationToken))!.Attempts);
        Assert.Equal(AgentRunStatus.Failed, await store.FailAsync(run.Id, "The agent finished without changing any files.", Usage, retry: true, _cancellationToken));

        var failed = (await store.GetAsync(run.Id, _cancellationToken))!;
        Assert.Equal((AgentRunStatus.Failed, "The agent finished without changing any files."), (failed.Status, failed.Error));
        Assert.Equal(2.5m, failed.AiCredits);
        Assert.Null(await store.FailAsync(run.Id, "Again.", null, retry: false, _cancellationToken));

        // The retry is a second run of the same task, each with its own outcome and cost.
        var task = Assert.Single(await store.GetForRepositoryAsync(Repo, 10, _cancellationToken));
        Assert.Equal(run.Id, task.Run.Id);
        Assert.Equal([2, 1], task.Runs.Select(r => r.Attempt));
        Assert.All(task.Runs, r => Assert.Equal((run.Id, AgentRunStatus.Failed), (r.AgentRunId, r.Status)));
        Assert.All(task.Runs, r => Assert.NotNull(r.CompletedAt));
        Assert.Equal(("The agent finished without changing any files.", 2.5m, "claude-sonnet-4.5"), (task.Runs[0].Error, task.Runs[0].AiCredits, task.Runs[0].Models));
        Assert.Equal(("git clone failed (128).", null), (task.Runs[1].Error, task.Runs[1].AiCredits));
        Assert.Equal(task.Runs[1].StartedAt, task.Run.StartedAt);
    }

    [Fact]
    public async Task ARunThatSucceeds_IsStoredAsARunOfItsTask_AndCancel_EndsTheRunThatIsGoingOn()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new AgentRunStore(database.Factory, TimeProvider.System);
        var run = await StartedAsync(store, maxAttempts: 2);
        var result = new AgentRunResult("dghsf/42-task-1a2b3c4d", 7, "https://github.com/owner/repo/pull/7", "Did it.", Usage, "Stopped at the credit limit.");
        Assert.True(await store.CompleteAsync(run.Id, result, _cancellationToken));
        var succeeded = Assert.Single(Assert.Single(await store.GetForRepositoryAsync(Repo, 10, _cancellationToken)).Runs);
        Assert.Equal((AgentRunStatus.Succeeded, 7, "dghsf/42-task-1a2b3c4d", "Did it.", "Stopped at the credit limit.", 2.5m),
            (succeeded.Status, succeeded.PullRequestNumber, succeeded.Branch, succeeded.Summary, succeeded.Error, succeeded.AiCredits));

        var other = await StartedAsync(store, maxAttempts: 2);
        Assert.True(await store.CancelAsync(Repo, other.Id, _cancellationToken));
        var cancelled = (await store.GetForRepositoryAsync(Repo, 10, _cancellationToken)).Single(t => t.Run.Id == other.Id);
        Assert.Equal(AgentRunStatus.Cancelled, cancelled.Run.Status);
        Assert.Equal(AgentRunStatus.Cancelled, Assert.Single(cancelled.Runs).Status);
    }

    [Fact]
    public async Task SessionUsage_IsStoredPerModel_CountedOnTheCatalogsDefaultTier_AndKeptWhenTheModelIsSavedOrDeleted()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var models = new AiModelStore(database.Factory, TimeProvider.System);
        var longContext = await models.SaveAsync(new AiModel { Name = "Claude Sonnet 4.5", ModelId = "claude-sonnet-4.5", Tier = "Long context" }, _cancellationToken);
        var sonnet = await models.SaveAsync(new AiModel { Name = "Claude Sonnet 4.5", ModelId = "claude-sonnet-4.5" }, _cancellationToken);
        var store = new AgentRunStore(database.Factory, TimeProvider.System);
        var sonnetUsage = new AgentRunModelUsage("claude-sonnet-4.5", 2_000_000_000, true, 3, 1m, 1000, 200, 700, 100, 50);
        var unknownUsage = new AgentRunModelUsage("gpt-unknown", 500_000_000, false, 1, 0.5m, 200, 100, 100, 0, 0);
        var run = await StartedAsync(store, maxAttempts: 2);

        // Two sessions: an attempt that failed after the agent ran, and the attempt that succeeded.
        Assert.Equal(AgentRunStatus.Pending, await store.FailAsync(run.Id, "Push failed.", Usage with { PerModel = [sonnetUsage, unknownUsage] }, retry: true, _cancellationToken));
        await store.ClaimDueAsync(10, _cancellationToken);
        await store.TryStartAsync(run.Id, Lease, _cancellationToken);
        var result = new AgentRunResult("dghsf/42-task-1a2b3c4d", 7, "https://github.com/owner/repo/pull/7", null, Usage with { PerModel = [sonnetUsage] });
        Assert.True(await store.CompleteAsync(run.Id, result, _cancellationToken));
        Assert.False(await store.CompleteAsync(run.Id, result, _cancellationToken));

        var counted = (await models.GetAsync(sonnet.Id, _cancellationToken))!;
        Assert.Equal(new AiModelUsageTotals(2, 4_000_000_000, 6, 2000, 400, 1400, 200, 100), counted.Usage);
        Assert.Equal(4m, counted.Usage.AiCredits);
        Assert.Equal(AiModelUsageTotals.None, (await models.GetAsync(longContext.Id, _cancellationToken))!.Usage);

        // Saving a copy loaded before the sessions doesn't reset the totals.
        var saved = await models.SaveAsync(sonnet with { InputPrice = 300m }, _cancellationToken);
        Assert.Equal((300m, 2), (saved.InputPrice, saved.Usage.Sessions));

        var page = await models.GetUsagePageAsync(new AiModelUsageQuery(sonnet.Id, 0, 10), _cancellationToken);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal([2, 1], page.Items.Select(u => u.Attempt));
        Assert.All(page.Items, u => Assert.Equal((run.Id, Repo, run.WorkItemId, 2m), (u.AgentRunId, u.RepositoryFullName, u.WorkItemId, u.AiCredits)));
        Assert.All(page.Items, u => Assert.Null(u.WorkItem));

        await models.DeleteAsync(sonnet.Id, _cancellationToken);
        await using var db = await database.Factory.CreateDbContextAsync(_cancellationToken);
        var rows = await db.AgentRunModelUsages.Where(u => u.AgentRunId == run.Id).ToListAsync(_cancellationToken);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, u => Assert.Null(u.AiModelId));
        Assert.False(rows.Single(u => u.Model == "gpt-unknown").Complete);
    }

    [Fact]
    public async Task Recover_TakesBackLostMessagesAndExpiredLeases_AndCancel_StopsTheLease()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var time = new TodoTaskTests.ManualTime(DateTimeOffset.UtcNow);
        var store = new AgentRunStore(database.Factory, time);
        var run = await store.CreateAsync(Repo, Guid.NewGuid(), null, 1, _cancellationToken);
        await store.ClaimDueAsync(10, _cancellationToken);

        // The message got lost: after the queued timeout the run is pending again.
        time.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(1, await store.RecoverAsync(TimeSpan.FromMinutes(15), _cancellationToken));
        Assert.Single(await store.ClaimDueAsync(10, _cancellationToken));

        // The job execution died on the last attempt: when its lease ends, the run fails.
        await store.TryStartAsync(run.Id, Lease, _cancellationToken);
        time.Advance(Lease + TimeSpan.FromMinutes(1));
        Assert.Equal(1, await store.RecoverAsync(TimeSpan.FromMinutes(15), _cancellationToken));
        var failed = (await store.GetAsync(run.Id, _cancellationToken))!;
        Assert.Equal(AgentRunStatus.Failed, failed.Status);
        Assert.Contains("attempt 1 of 1", failed.Error);

        var other = await StartedAsync(store, maxAttempts: 1);
        Assert.True(await store.CancelAsync("OWNER/repo", other.Id, _cancellationToken));
        Assert.False(await store.RenewLeaseAsync(other.Id, Lease, _cancellationToken));
        Assert.False(await store.CancelAsync(Repo, other.Id, _cancellationToken));
        Assert.Null(await store.GetLatestAsync(Repo, other.WorkItemId, _cancellationToken));
    }

    [Fact]
    public async Task Execute_ClonesRunsTheAgentPushesAndOpensThePullRequest_AndRecordsTheCost()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var worker = await WorkerAsync(database);
        worker.Agent.Outcome = new("Added the endpoint and its tests.", Usage, null);

        var status = await worker.ExecuteAsync(_cancellationToken);

        Assert.Equal(AgentRunStatus.Succeeded, status);
        var run = (await worker.Runs.GetAsync(worker.Run.Id, _cancellationToken))!;
        Assert.Equal((7, 2.5m), (run.PullRequestNumber, run.AiCredits));
        Assert.StartsWith("dghsf/42-add-the-endpoint-", run.Branch);
        Assert.Equal(["clone main " + run.Branch, "changes origin/main", "commit", "push " + run.Branch], worker.Git.Commands);
        Assert.Equal(AgentRunKind.Implement, run.Kind);
        Assert.NotNull(run.FeedbackUntil);
        var request = worker.Agent.Request!;
        Assert.Equal(("claude-sonnet-4.5", AgentRunPrompt.Instructions), (request.Model, request.Instructions));
        Assert.Contains("# Add the endpoint", request.Prompt);
        Assert.Contains("Implement GitHub issue #42", request.Prompt);

        var pullRequest = JsonDocument.Parse(worker.GitHub.PullRequestBody!).RootElement;
        Assert.Equal((run.Branch, "main", false), (pullRequest.GetProperty("head").GetString(), pullRequest.GetProperty("base").GetString(), pullRequest.GetProperty("draft").GetBoolean()));
        var body = pullRequest.GetProperty("body").GetString()!;
        Assert.Contains("Added the endpoint and its tests.", body);
        Assert.Contains("Closes #42", body);
        Assert.Contains("2.5 AI credits", body);
        Assert.False(Directory.Exists(worker.Folder));
    }

    [Fact]
    public async Task Execute_WhenTheAgentStoppedEarly_PushesADraft_AndWithoutChangesFailsWithoutRetry()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var worker = await WorkerAsync(database);
        worker.Agent.Outcome = new("Half way.", Usage, "The agent reached the limit of 2 AI credits per run (2.5 used) before it finished.");

        Assert.Equal(AgentRunStatus.Succeeded, await worker.ExecuteAsync(_cancellationToken));
        Assert.True(JsonDocument.Parse(worker.GitHub.PullRequestBody!).RootElement.GetProperty("draft").GetBoolean());
        Assert.Contains("reached the limit", (await worker.Runs.GetAsync(worker.Run.Id, _cancellationToken))!.Error);

        await using var second = await WorkerAsync(database);
        second.Git.HasChanges = false;
        second.Agent.Outcome = new("Nothing to do: it already works.", Usage, null);

        Assert.Equal(AgentRunStatus.Failed, await second.ExecuteAsync(_cancellationToken));
        var failed = (await second.Runs.GetAsync(second.Run.Id, _cancellationToken))!;
        Assert.Contains("without changing any files. It said: Nothing to do", failed.Error);
        Assert.Equal(2.5m, failed.AiCredits);
        Assert.DoesNotContain("push", string.Join(" ", second.Git.Commands));
    }

    [Fact]
    public async Task Execute_WhenTheCloneFails_GivesTheRunBackForAnotherAttempt()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var worker = await WorkerAsync(database);
        worker.Git.CloneError = new GitCommandException("git clone failed (128): could not resolve host");

        Assert.Equal(AgentRunStatus.Pending, await worker.ExecuteAsync(_cancellationToken));
        Assert.Null(worker.Agent.Request);
        Assert.Null((await worker.Runs.GetAsync(worker.Run.Id, _cancellationToken))!.NanoAiu);
    }

    [Fact]
    public async Task Execute_ACancelledRun_StopsTheAgentAndPushesNothing()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var worker = await WorkerAsync(database, lease: TimeSpan.FromMilliseconds(300));
        worker.Agent.OnRun = async cancellationToken =>
        {
            await worker.Runs.CancelAsync(Repo, worker.Run.Id, CancellationToken.None);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        };

        Assert.Equal(AgentRunStatus.Cancelled, await worker.ExecuteAsync(_cancellationToken));
        Assert.Equal(AgentRunStatus.Cancelled, (await worker.Runs.GetAsync(worker.Run.Id, _cancellationToken))!.Status);
        Assert.DoesNotContain("push", string.Join(" ", worker.Git.Commands));
        Assert.Null(worker.GitHub.PullRequestBody);
    }

    [Fact]
    public async Task Execute_ARevision_MergesTheBase_PushesToThePullRequestsBranch_AndAnswersTheNewFeedback()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var worker = await WorkerAsync(database);
        worker.Agent.Outcome = new("Added the endpoint.", Usage, null);
        Assert.Equal(AgentRunStatus.Succeeded, await worker.ExecuteAsync(_cancellationToken));
        var now = DateTimeOffset.UtcNow;
        await HandledUntilAsync(database, worker.Run.Id, now.AddHours(-1));
        worker.GitHub.IssueComments = Comments(
            (1, "Old remark", now.AddHours(-2), null),
            (2, "Please also log it.", now.AddMinutes(-30), null),
            (3, $"Answered before.\n\n{PullRequestDiscussion.Marker(Guid.NewGuid())}", now.AddMinutes(-20), null));
        worker.GitHub.ReviewComments = Comments((100, "Rename this", now.AddMinutes(-35), "src/App.cs"));
        worker.Git.Merge = new(GitMergeOutcome.Merged, []);
        worker.Git.Commands.Clear();
        worker.Agent.OnRun = async cancellationToken =>
        {
            var tool = Assert.Single(worker.Agent.Request!.Tools!);
            var unknown = await tool.InvokeAsync(new AIFunctionArguments { ["feedbackId"] = "F9", ["reply"] = "?" }, cancellationToken);
            Assert.Contains("F1, F2", unknown?.ToString());
            await tool.InvokeAsync(new AIFunctionArguments { ["feedbackId"] = "f1", ["reply"] = "Renamed it to Endpoint." }, cancellationToken);
        };
        worker.Agent.Outcome = new("Renamed the endpoint.", Usage, null);

        var revision = await worker.StartRevisionAsync(_cancellationToken);
        Assert.Equal(AgentRunStatus.Succeeded, await worker.ExecuteAsync(revision, _cancellationToken));

        Assert.Equal(["clone-branch dghsf/42-add-the-endpoint-review main", "merge main", "changes abc123", "commit", "push dghsf/42-add-the-endpoint-review"],
            worker.Git.Commands);
        var prompt = worker.Agent.Request!.Prompt;
        Assert.Contains("Revise pull request #7 (\"Add the endpoint\")", prompt);
        Assert.Contains("DGHSF merged the latest `main` into the branch", prompt);
        Assert.Contains("### F1 · line comment by @reviewer on `src/App.cs` line 3\n\nRename this\n", prompt);
        Assert.Contains("### F2 · comment by @reviewer\n\nPlease also log it.\n", prompt);
        Assert.Contains("## Earlier discussion", prompt);
        Assert.Contains("Old remark", prompt);
        Assert.DoesNotContain("Answered before.", prompt);
        Assert.Contains("# Add the endpoint", prompt);

        var reply = Assert.Single(worker.GitHub.Posted, r => r.Path == "/repos/owner/repo/pulls/7/comments/100/replies");
        Assert.StartsWith("<sub>🤖 **DGHSF** on behalf of [djohnnie](https://github.com/djohnnie)</sub>\n\nRenamed it to Endpoint.", Body(reply.Body));
        Assert.Contains("<!-- dghsf:agent-run:" + revision.Id, Body(reply.Body));
        var comment = Body(Assert.Single(worker.GitHub.Posted, r => r.Path == "/repos/owner/repo/issues/7/comments").Body);
        Assert.StartsWith("<sub>🤖 **DGHSF** on behalf of [djohnnie](https://github.com/djohnnie)</sub>\n\nRenamed the endpoint.", comment);
        Assert.Contains("> Please also log it.", comment);
        Assert.Contains("_The agent didn't answer this._", comment);
        Assert.Contains("Pushed the changes to `dghsf/42-add-the-endpoint-review`. Merged the latest `main` into the branch.", comment);
        Assert.Contains("Revised by the DGHSF agent worker", comment);
        // Only the first run opened a pull request.
        Assert.Single(worker.GitHub.Posted, r => r.Path == "/repos/owner/repo/pulls");

        Assert.Equal(worker.Run.Id, revision.Id);
        var revised = (await worker.Runs.GetAsync(revision.Id, _cancellationToken))!;
        // The task's cost adds up the run that opened the pull request and the revision.
        Assert.Equal((AgentRunKind.Revise, 7, "dghsf/42-add-the-endpoint-review", 5m), (revised.Kind, revised.PullRequestNumber, revised.Branch, revised.AiCredits));
        Assert.True(revised.FeedbackUntil > now.AddMinutes(-1));
        Assert.Null(revised.Error);
    }

    [Fact]
    public async Task Execute_ARevisionWithoutNewFeedback_FailsBeforeTheAgentRuns()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var worker = await WorkerAsync(database);
        Assert.Equal(AgentRunStatus.Succeeded, await worker.ExecuteAsync(_cancellationToken));
        worker.GitHub.IssueComments = Comments((1, "Before the pull request was revised", DateTimeOffset.UtcNow.AddDays(-1), null));
        var calls = worker.Agent.Calls;

        var revision = await worker.StartRevisionAsync(_cancellationToken);
        Assert.Equal(AgentRunStatus.Failed, await worker.ExecuteAsync(revision, _cancellationToken));

        Assert.Equal(calls, worker.Agent.Calls);
        var failed = (await worker.Runs.GetAsync(revision.Id, _cancellationToken))!;
        Assert.Contains("no new feedback on pull request #7", failed.Error);
        var run = Assert.Single(await worker.Runs.GetForRepositoryAsync(Repo, 10, _cancellationToken)).Runs[0];
        Assert.Equal((2, AgentRunKind.Revise, (decimal?)null), (run.Attempt, run.Kind, run.AiCredits));
    }

    [Fact]
    public async Task Execute_ARevisionThatLeavesConflictMarkers_PushesNothing()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var worker = await WorkerAsync(database);
        Assert.Equal(AgentRunStatus.Succeeded, await worker.ExecuteAsync(_cancellationToken));
        worker.GitHub.IssueComments = Comments((1, "Fix the typo.", DateTimeOffset.UtcNow.AddMinutes(-1), null));
        await HandledUntilAsync(database, worker.Run.Id, DateTimeOffset.UtcNow.AddHours(-1));
        worker.Git.Merge = new(GitMergeOutcome.Conflicts, ["src/App.cs", "src/Deleted.cs"]);
        worker.Git.Commands.Clear();
        worker.Agent.OnRun = async cancellationToken =>
        {
            var file = Path.Combine(worker.Agent.Request!.WorkingDirectory, "src", "App.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> origin/main\n", cancellationToken);
        };
        worker.Agent.Outcome = new("Fixed it.", Usage, null);

        var revision = await worker.StartRevisionAsync(_cancellationToken);
        Assert.Equal(AgentRunStatus.Failed, await worker.ExecuteAsync(revision, _cancellationToken));

        Assert.Contains("has conflicts in:\n\n- `src/App.cs`\n- `src/Deleted.cs`\n", worker.Agent.Request!.Prompt);
        Assert.Contains("left merge conflicts with main in src/App.cs, so nothing was pushed", (await worker.Runs.GetAsync(revision.Id, _cancellationToken))!.Error);
        Assert.DoesNotContain("push", string.Join(" ", worker.Git.Commands));
        Assert.DoesNotContain(worker.GitHub.Posted, r => r.Path.Contains("/comments"));
    }

    [Fact]
    public void Discussion_NumbersTheFeedbackSinceTheLastRun_AndLeavesOutTheWorkersOwnComments()
    {
        var start = DateTimeOffset.UnixEpoch;
        static GitHubOwner Owner(string login)
        {
            return new GitHubOwner(login, null);
        }

        var discussion = PullRequestDiscussion.Split(
            [
                new GitHubComment(1, Owner("alice"), "Old", start.AddHours(1), "u1"),
                new GitHubComment(2, Owner("bob"), "A question?", start.AddHours(3), "u2"),
                new GitHubComment(3, Owner("worker"), $"Answer\n\n{PullRequestDiscussion.Marker(Guid.NewGuid())}", start.AddHours(4), "u3"),
                new GitHubComment(4, Owner("late"), "Too late for this run", start.AddHours(9), "u4")
            ],
            [
                new GitHubComment(100, Owner("alice"), "Rename", start.AddHours(2.5), "u100", "src/App.cs", null, 7),
                new GitHubComment(101, Owner("bob"), "Agreed", start.AddHours(3.5), "u101", "src/App.cs", 7, InReplyToId: 100)
            ],
            [
                new GitHubPullRequestReview(Owner("carol"), start.AddHours(2.75), "CHANGES_REQUESTED", "r9", 9, "Needs tests"),
                new GitHubPullRequestReview(Owner("dave"), start.AddHours(2.8), "COMMENTED", "r10", 10, null)
            ],
            since: start.AddHours(2), until: start.AddHours(5));

        Assert.Equal(["F1", "F2", "F3", "F4"], discussion.Feedback.Select(f => f.Id));
        Assert.Equal((PullRequestDiscussionKind.LineComment, 7, 100L), (discussion.Feedback[0].Kind, discussion.Feedback[0].Line, discussion.Feedback[0].ThreadId));
        Assert.Equal((PullRequestDiscussionKind.Review, "(Requested changes.) Needs tests"), (discussion.Feedback[1].Kind, discussion.Feedback[1].Body));
        Assert.Equal(("A question?", "bob"), (discussion.Feedback[2].Body, discussion.Feedback[2].Author));
        Assert.Equal(("Agreed", 100L), (discussion.Feedback[3].Body, discussion.Feedback[3].ThreadId));
        Assert.Equal(["Old"], discussion.Earlier.Select(e => e.Body));
    }

    [Fact]
    public void HandledUntil_IsTheLatestSuccessOnThePullRequest_AlsoWhileALaterRunOfItsTaskGoesOn()
    {
        var start = DateTimeOffset.UnixEpoch;
        AgentRun[] runs =
        [
            new() { Id = Guid.NewGuid(), Status = AgentRunStatus.Succeeded, PullRequestNumber = 7, CompletedAt = start.AddHours(1) },
            new() { Id = Guid.NewGuid(), Status = AgentRunStatus.Failed, PullRequestNumber = 7, CompletedAt = start.AddHours(6) },
            new() { Id = Guid.NewGuid(), Status = AgentRunStatus.Succeeded, PullRequestNumber = 8, CompletedAt = start.AddHours(7) }
        ];
        AgentRun running = new() { Id = Guid.NewGuid(), Status = AgentRunStatus.Running, PullRequestNumber = 7, FeedbackUntil = start.AddHours(3) };

        Assert.Equal(start.AddHours(1), PullRequestDiscussion.HandledUntil(runs, 7));
        Assert.Equal(start.AddHours(3), PullRequestDiscussion.HandledUntil([.. runs, running], 7));
        Assert.Equal(DateTimeOffset.MinValue, PullRequestDiscussion.HandledUntil(runs, 9));
    }

    [Fact]
    public void Prompt_BranchAndPullRequest_DescribeTheIssue()
    {
        var item = new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Story, Title = "Log in with a passkey (WebAuthn) — the whole flow, end to end!",
            Description = "As a user…", TechnicalDetails = "Use the existing AuthController.", CustomInstructions = "Keep it small.", IssueNumber = 42
        };
        var epic = new WorkItem { Type = WorkItemType.Epic, Title = "Passkeys", IssueNumber = 10 };
        var feature = new WorkItem { Type = WorkItemType.Feature, Title = "Passkey login", IssueNumber = 11 };
        var runId = Guid.Parse("1a2b3c4d-0000-0000-0000-000000000000");

        var branch = AgentRunPrompt.BranchName(item, runId);
        Assert.Equal("dghsf/42-log-in-with-a-passkey-webauthn-the-whole-1a2b3c4d", branch);

        var prompt = AgentRunPrompt.Build(item, [feature, epic], branch, "main");
        Assert.Contains("Implement GitHub issue #42 of the repository owner/repo: a user story.", prompt);
        Assert.Contains("## Technical details\n\nUse the existing AuthController.", prompt);
        Assert.Contains("## Additional instructions\n\nKeep it small.", prompt);
        Assert.Contains("- Epic #10: Passkeys\n- Feature #11: Passkey login\n", prompt);

        var body = AgentRunPrompt.PullRequestBody(item, runId, null, Usage with { Complete = false }, "Stopped early.", "djohnnie");
        Assert.StartsWith("<sub>🤖 **DGHSF** on behalf of [djohnnie](https://github.com/djohnnie)</sub>\n\n> [!WARNING]\n> Stopped early.", body);
        Assert.Contains("_The agent didn't write a summary._", body);
        Assert.Contains("2.5 AI credits (at least)", body);
    }

    [Fact]
    public void TheAgentsEnvironment_LeavesOutTheWorkersSecrets()
    {
        var environment = AgentEnvironment.Sanitized(new System.Collections.Hashtable
        {
            ["PATH"] = "/usr/bin", ["HOME"] = "/home/app", ["DOTNET_NOLOGO"] = "1",
            ["ConnectionStrings__Database"] = "Server=sql", ["ServiceBus"] = "Endpoint=sb://x", ["ServiceBus__fullyQualifiedNamespace"] = "x",
            ["AgentWorker__GitHubToken"] = "ghp", ["IDENTITY_ENDPOINT"] = "http://localhost:42356", ["IDENTITY_HEADER"] = "h",
            ["NPM_TOKEN"] = "t", ["MY_API_KEY"] = "k", ["Some__Password"] = "p", ["GH_TOKEN"] = "g"
        });

        Assert.Equal(["DOTNET_NOLOGO", "HOME", "PATH"], environment.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void QueueMessages_CarryTheRunId()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id.ToString(), AgentRunQueue.Message(id).MessageId);
        Assert.Equal(id, AgentRunQueue.RunId(AgentRunQueue.Message(id).Body.ToString()));
        Assert.Equal(id, AgentRunQueue.RunId($"\"{id}\""));
        Assert.Null(AgentRunQueue.RunId("hello"));
    }

    [Fact]
    public void EvaluateWorker_FollowsTheRunUntilItSucceeds_ThenThePullRequest()
    {
        static AgentRun Run(AgentRunStatus status)
        {
            return new AgentRun { Status = status, PullRequestNumber = 7, PullRequestUrl = "https://github.com/owner/repo/pull/7", Branch = "dghsf/42-x" };
        }
        static GitHubLinkedPullRequest PullRequest(string state)
        {
            return new GitHubLinkedPullRequest(7, "https://github.com/owner/repo/pull/7", "Task", state, false, "dghsf/42-x", DateTimeOffset.UtcNow, null, [], "SUCCESS");
        }

        Assert.Equal(CopilotProgress.None, CopilotProgressEvaluator.EvaluateWorker(false, null, null).Progress);
        Assert.Equal(CopilotProgress.Queued, CopilotProgressEvaluator.EvaluateWorker(false, Run(AgentRunStatus.Queued), null).Progress);
        Assert.Equal(CopilotProgress.Working, CopilotProgressEvaluator.EvaluateWorker(false, Run(AgentRunStatus.Running), PullRequest("OPEN")).Progress);
        Assert.Equal(CopilotProgress.Stopped, CopilotProgressEvaluator.EvaluateWorker(false, Run(AgentRunStatus.Failed), null).Progress);
        Assert.Equal((CopilotProgress.ReadyForReview, 7), Pick(CopilotProgressEvaluator.EvaluateWorker(false, Run(AgentRunStatus.Succeeded), null)));
        Assert.Equal((CopilotProgress.Merged, 7), Pick(CopilotProgressEvaluator.EvaluateWorker(true, Run(AgentRunStatus.Failed), PullRequest("MERGED"))));
        // A new run that works on an item whose earlier pull request was closed shows the new run.
        Assert.Equal(CopilotProgress.Working, CopilotProgressEvaluator.EvaluateWorker(false, Run(AgentRunStatus.Running), PullRequest("CLOSED")).Progress);

        static (CopilotProgress, int?) Pick(WorkItemProgressSnapshot snapshot)
        {
            return (snapshot.Progress, snapshot.PullRequestNumber);
        }
    }

    private async Task<AgentRun> StartedAsync(AgentRunStore store, int maxAttempts)
    {
        var run = await store.CreateAsync(Repo, Guid.NewGuid(), null, maxAttempts, _cancellationToken);
        await store.ClaimDueAsync(10, _cancellationToken);
        return (await store.TryStartAsync(run.Id, Lease, _cancellationToken))!;
    }

    /// <summary>Sets until when a task handled its pull request's discussion, as if it ended then.</summary>
    private async Task HandledUntilAsync(SqlServerDatabase database, Guid runId, DateTimeOffset until)
    {
        await using var db = await database.Factory.CreateDbContextAsync(_cancellationToken);
        var run = await db.AgentRuns.SingleAsync(r => r.Id == runId, _cancellationToken);
        run.FeedbackUntil = until;
        await db.SaveChangesAsync(_cancellationToken);
    }

    /// <summary>Comments by @reviewer as GitHub lists them; with a path they are comments on line 3 of that file.</summary>
    private static string Comments(params (long Id, string Body, DateTimeOffset CreatedAt, string? Path)[] comments)
    {
        return JsonSerializer.Serialize(comments.Select(c => new
        {
            id = c.Id, user = new { login = "reviewer" }, body = c.Body, created_at = c.CreatedAt, html_url = $"https://github.com/owner/repo/pull/7#c{c.Id}",
            path = c.Path, line = c.Path is null ? (int?)null : 3
        }));
    }

    private static string Body(string? json)
    {
        return JsonDocument.Parse(json!).RootElement.GetProperty("body").GetString()!;
    }

    /// <summary>A started run of an in-progress task with issue #42, and an executor with fake git, agent and GitHub.</summary>
    private async Task<WorkerFixture> WorkerAsync(SqlServerDatabase database, TimeSpan? lease = null)
    {
        var gitHub = new FakeGitHub();
        var git = new FakeGit();
        var agent = new FakeAgent();
        var folder = Path.Combine(Path.GetTempPath(), "dghsf-agent-worker-tests", Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton(database.Factory)
            .AddSingleton(new GitHubApiClient(new HttpClient(gitHub) { BaseAddress = new Uri("https://api.github.com/") }, new ConfiguredGitHubTokenProvider("token")))
            .Configure<AgentRunOptions>(o => o.Lease = lease ?? Lease)
            .Configure<AgentWorkerOptions>(o =>
            {
                o.GitHubToken = "token";
                o.WorkDirectory = folder;
            })
            .AddSingleton<IGitWorkspace>(git)
            .AddSingleton<ICodingAgent>(agent)
            .AddScoped<AgentRunExecutor>()
            .BuildServiceProvider();

        var item = await services.GetRequiredService<IWorkItemStore>().SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Add the endpoint", Description = "An endpoint.",
            Executor = AgentExecutor.DghsfWorker, Model = "claude-sonnet-4.5"
        }, _cancellationToken);
        await using (var db = await database.Factory.CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(e => e.Id == item.Id, _cancellationToken);
            entity.Status = WorkItemStatus.InProgress;
            entity.IssueNumber = 42;
            entity.IssueId = 1042;
            entity.IssueUrl = "https://github.com/owner/repo/issues/42";
            await db.SaveChangesAsync(_cancellationToken);
        }

        var runs = services.GetRequiredService<IAgentRunStore>();
        var created = await runs.CreateAsync(Repo, item.Id, item.Model, 2, _cancellationToken);
        await runs.ClaimDueAsync(10, _cancellationToken);
        var run = (await runs.TryStartAsync(created.Id, lease ?? Lease, _cancellationToken))!;
        return new WorkerFixture(services, run, runs, git, agent, gitHub, Path.Combine(folder, run.Id.ToString("N")));
    }

    private sealed record WorkerFixture(ServiceProvider Services, AgentRun Run, IAgentRunStore Runs, FakeGit Git, FakeAgent Agent, FakeGitHub GitHub, string Folder)
        : IAsyncDisposable
    {
        public async Task<AgentRunStatus?> ExecuteAsync(CancellationToken cancellationToken)
        {
            return await ExecuteAsync(Run, cancellationToken);
        }

        public async Task<AgentRunStatus?> ExecuteAsync(AgentRun run, CancellationToken cancellationToken)
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AgentRunExecutor>().ExecuteAsync(run, cancellationToken);
        }

        /// <summary>A started revision of pull request #7, which the first run opened.</summary>
        public async Task<AgentRun> StartRevisionAsync(CancellationToken cancellationToken)
        {
            var created = await Runs.CreateRevisionAsync(Repo, Run.WorkItemId, "claude-sonnet-4.5", 2, "dghsf/42-add-the-endpoint-review", 7,
                "https://github.com/owner/repo/pull/7", cancellationToken);
            await Runs.ClaimDueAsync(10, cancellationToken);
            return (await Runs.TryStartAsync(created.Id, Lease, cancellationToken))!;
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
        }
    }

    private sealed class FakeGit : IGitWorkspace
    {
        public List<string> Commands { get; } = [];
        public bool HasChanges { get; set; } = true;
        public Exception? CloneError { get; set; }
        public GitMergeResult Merge { get; set; } = GitMergeResult.UpToDate;

        public Task CloneAsync(string repositoryFullName, string branch, string newBranch, string directory, string token, CancellationToken cancellationToken)
        {
            if (CloneError is not null)
            {
                throw CloneError;
            }
            Commands.Add($"clone {branch} {newBranch}");
            Directory.CreateDirectory(directory);
            return Task.CompletedTask;
        }

        public Task<string> CloneBranchAsync(string repositoryFullName, string branch, string baseBranch, string directory, string token, CancellationToken cancellationToken)
        {
            Commands.Add($"clone-branch {branch} {baseBranch}");
            Directory.CreateDirectory(directory);
            return Task.FromResult("abc123");
        }

        public Task<GitMergeResult> MergeBaseAsync(string directory, string baseBranch, string authorName, string authorEmail, CancellationToken cancellationToken)
        {
            Commands.Add($"merge {baseBranch}");
            return Task.FromResult(Merge);
        }

        public Task<bool> HasChangesAsync(string directory, string since, CancellationToken cancellationToken)
        {
            Commands.Add($"changes {since}");
            return Task.FromResult(HasChanges);
        }

        public Task CommitAllAsync(string directory, string message, string authorName, string authorEmail, CancellationToken cancellationToken)
        {
            Commands.Add("commit");
            return Task.CompletedTask;
        }

        public Task PushAsync(string directory, string branch, string token, CancellationToken cancellationToken)
        {
            Commands.Add($"push {branch}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAgent : ICodingAgent
    {
        public CodingAgentRequest? Request { get; private set; }
        public CodingAgentOutcome Outcome { get; set; } = new(null, AgentRunUsage.None, null);
        public Func<CancellationToken, Task>? OnRun { get; set; }
        public int Calls { get; private set; }

        public async Task<CodingAgentOutcome> RunAsync(CodingAgentRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            Calls++;
            if (OnRun is not null)
            {
                await OnRun(cancellationToken);
            }
            return Outcome;
        }
    }

    /// <summary>
    /// The repository, pull request #7 and its discussion; everything else (the refresh) is a 404, which the worker shrugs off.
    /// The pull request's branch is <c>dghsf/42-add-the-endpoint-review</c>.
    /// </summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public string? PullRequestBody { get; private set; }

        /// <summary>Every request that posted something, in order.</summary>
        public List<(string Path, string? Body)> Posted { get; } = [];

        public string IssueComments { get; set; } = "[]";
        public string ReviewComments { get; set; } = "[]";
        public string Reviews { get; set; } = "[]";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                Posted.Add((path, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            }
            if (request.Method == HttpMethod.Get)
            {
                switch (path)
                {
                    case "/user":
                        return Json("""{"login":"djohnnie","avatar_url":null,"type":"User"}""");
                    case "/repos/owner/repo/pulls/7":
                        return Json("""{"number":7,"title":"Add the endpoint","html_url":"https://github.com/owner/repo/pull/7","state":"open","draft":false,"user":null,"merged_at":null,"additions":1,"deletions":0,"changed_files":1,"head":{"ref":"dghsf/42-add-the-endpoint-review","sha":"abc123"},"base":{"ref":"main","sha":"def456"}}""");
                    case "/repos/owner/repo/issues/7/comments":
                        return Json(IssueComments);
                    case "/repos/owner/repo/pulls/7/comments":
                        return Json(ReviewComments);
                    case "/repos/owner/repo/pulls/7/reviews":
                        return Json(Reviews);
                }
            }
            if (request.Method == HttpMethod.Post && (path == "/repos/owner/repo/issues/7/comments" || path.EndsWith("/replies", StringComparison.Ordinal)))
            {
                return Json("""{"html_url":"https://github.com/owner/repo/pull/7#issuecomment-5"}""", HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get && path == "/repos/owner/repo")
            {
                return Json("""{"id":1,"name":"repo","full_name":"owner/repo","owner":{"login":"owner","avatar_url":null},"html_url":"https://github.com/owner/repo","description":null,"private":false,"fork":false,"archived":false,"language":"C#","default_branch":"main","pushed_at":null}""");
            }
            if (request.Method == HttpMethod.Post && path == "/repos/owner/repo/pulls")
            {
                PullRequestBody = Posted[^1].Body;
                return Json("""{"number":7,"title":"Add the endpoint","html_url":"https://github.com/owner/repo/pull/7","state":"open","draft":false,"user":null,"merged_at":null,"additions":1,"deletions":0,"changed_files":1}""",
                    HttpStatusCode.Created);
            }
            return Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

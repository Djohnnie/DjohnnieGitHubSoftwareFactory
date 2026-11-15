using Djohnnie.SoftwareFactory.Application.Features.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>
/// The TODO task life cycle against a real SQL Server: the store's conditional updates (claims, leases, fan-in) are
/// what keeps duplicate Service Bus messages and crashed executors harmless.
/// </summary>
public class TodoTaskTests
{
    private const string Repo = "owner/repo";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(30);
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARun_WaitsForItsSubTasks_AndFailsWhenOneOfThemFails()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new TodoTaskStore(database.Factory, TimeProvider.System);
        var root = await store.CreateRunAsync("Owner/Repo", NewTask("Analyze"), "octocat", 1, _cancellationToken);

        Assert.Equal(root.Id, root.RootId);
        Assert.Equal(Repo, root.RepositoryFullName);
        Assert.Equal([root.Id], await store.ClaimDueAsync(10, _cancellationToken));
        Assert.Empty(await store.ClaimDueAsync(10, _cancellationToken));
        Assert.NotNull(await store.TryStartAsync(root.Id, Lease, _cancellationToken));
        Assert.True(await store.CompleteAsync(root.Id, "Planned 2 features.", [NewTask("Feature A"), NewTask("Feature B")], _cancellationToken));

        var run = (await store.GetRunAsync(Repo, root.Id, _cancellationToken))!;
        Assert.Equal(TodoTaskStatus.Waiting, run.Root.Status);
        Assert.Equal(2, run.Total);
        Assert.All(run.SubTasks, sub => Assert.Equal((root.Id, root.Id, TodoTaskStatus.Pending), (sub.RootId, sub.ParentId!.Value, sub.Status)));

        var ids = await store.ClaimDueAsync(10, _cancellationToken);
        Assert.Equal(2, ids.Count);
        foreach (var id in ids)
        {
            await store.TryStartAsync(id, Lease, _cancellationToken);
        }
        Assert.True(await store.CompleteAsync(ids[0], "Wrote 3 user stories.", [], _cancellationToken));
        Assert.Equal(TodoTaskStatus.Waiting, (await store.GetStatusAsync(root.Id, _cancellationToken)));
        Assert.Equal(TodoTaskStatus.Failed, await store.FailAsync(ids[1], "The model timed out.", DateTimeOffset.UtcNow, _cancellationToken));

        run = (await store.GetRunAsync(Repo, root.Id, _cancellationToken))!;
        Assert.Equal((TodoTaskStatus.Failed, "1 of 2 sub-tasks failed.", "Planned 2 features."), (run.Root.Status, run.Root.Error, run.Root.Result));
        Assert.NotNull(run.Root.CompletedAt);
        Assert.False(run.IsActive);
        Assert.Equal((1, 1), (run.Succeeded, run.Failed));
    }

    [Fact]
    public async Task ARun_Succeeds_WhenAllSubTasksSucceed_AndATaskWithoutSubTasksSucceedsRightAway()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new TodoTaskStore(database.Factory, TimeProvider.System);
        var root = await StartedRunAsync(store);
        await store.CompleteAsync(root.Id, null, [NewTask("Only one")], _cancellationToken);
        var sub = Assert.Single(await store.ClaimDueAsync(10, _cancellationToken));
        await store.TryStartAsync(sub, Lease, _cancellationToken);

        Assert.True(await store.CompleteAsync(sub, "Done.", [], _cancellationToken));

        Assert.Equal(TodoTaskStatus.Succeeded, await store.GetStatusAsync(sub, _cancellationToken));
        Assert.Equal(TodoTaskStatus.Succeeded, await store.GetStatusAsync(root.Id, _cancellationToken));
    }

    [Fact]
    public async Task TryStart_OfADuplicateMessage_ReturnsNull_AndConcurrentClaimsQueueATaskOnce()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new TodoTaskStore(database.Factory, TimeProvider.System);
        for (var i = 0; i < 5; i++)
        {
            await store.CreateRunAsync($"owner/repo{i}", NewTask("Analyze"), null, 3, _cancellationToken);
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => store.ClaimDueAsync(10, _cancellationToken))));
        var claimed = claims.SelectMany(ids => ids).ToList();
        Assert.Equal(5, claimed.Count);
        Assert.Equal(5, claimed.Distinct().Count());

        var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => store.TryStartAsync(claimed[0], Lease, _cancellationToken))));
        var started = Assert.Single(starts, task => task is not null)!;
        Assert.Equal((TodoTaskStatus.Running, 1), (started.Status, started.Attempts));
    }

    [Fact]
    public async Task Fail_WithAttemptsLeft_RetriesFromTheGivenMoment_AndRecover_TakesBackExpiredLeasesAndLostMessages()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var store = new TodoTaskStore(database.Factory, time);
        var root = await StartedRunAsync(store, maxAttempts: 2);

        Assert.Equal(TodoTaskStatus.Pending, await store.FailAsync(root.Id, "GitHub returned 502.", time.GetUtcNow().AddMinutes(1), _cancellationToken));
        Assert.Empty(await store.ClaimDueAsync(10, _cancellationToken));
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Single(await store.ClaimDueAsync(10, _cancellationToken));

        // The message got lost: after the queued timeout the task is pending again.
        time.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(1, await store.RecoverAsync(TimeSpan.FromMinutes(15), _cancellationToken));
        Assert.Single(await store.ClaimDueAsync(10, _cancellationToken));

        // The executor crashed on the last attempt: when its lease ends, the task fails.
        var started = (await store.TryStartAsync(root.Id, Lease, _cancellationToken))!;
        Assert.Equal(2, started.Attempts);
        time.Advance(Lease + TimeSpan.FromMinutes(1));
        Assert.Equal(1, await store.RecoverAsync(TimeSpan.FromMinutes(15), _cancellationToken));
        var run = (await store.GetRunAsync(Repo, root.Id, _cancellationToken))!;
        Assert.Equal(TodoTaskStatus.Failed, run.Root.Status);
        Assert.Contains("attempt 2 of 2", run.Root.Error);
        Assert.NotNull(run.Root.CompletedAt);
    }

    [Fact]
    public async Task CancelRun_CancelsWhatHasNotEnded_AndDiscardsTheResultOfARunningTask()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new TodoTaskStore(database.Factory, TimeProvider.System);
        var root = await StartedRunAsync(store);
        await store.CompleteAsync(root.Id, null, [NewTask("A"), NewTask("B")], _cancellationToken);
        var ids = await store.ClaimDueAsync(10, _cancellationToken);
        await store.TryStartAsync(ids[0], Lease, _cancellationToken);

        Assert.True(await store.HasActiveRunAsync(Repo, TodoTaskKind.AnalyzeRepository, _cancellationToken));
        Assert.Equal(0, await store.CancelRunAsync("owner/other", root.Id, _cancellationToken));
        Assert.Equal(3, await store.CancelRunAsync(Repo, root.Id, _cancellationToken));

        Assert.False(await store.CompleteAsync(ids[0], "Too late.", [NewTask("C")], _cancellationToken));
        Assert.Null(await store.TryStartAsync(ids[1], Lease, _cancellationToken));
        var run = (await store.GetRunAsync(Repo, root.Id, _cancellationToken))!;
        Assert.All([run.Root, .. run.SubTasks], task => Assert.Equal(TodoTaskStatus.Cancelled, task.Status));
        Assert.Equal(2, run.Total);
        Assert.False(await store.HasActiveRunAsync(Repo, TodoTaskKind.AnalyzeRepository, _cancellationToken));
    }

    [Fact]
    public async Task Executor_RunsTheHandler_RetriesErrors_FailsDomainRuleRefusals_AndIgnoresDuplicateMessages()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new TodoTaskStore(database.Factory, TimeProvider.System);
        var handler = new ScriptedHandler();
        var options = Options.Create(new TodoTaskOptions { RetryDelay = TimeSpan.FromMinutes(1) });
        var executor = new TodoTaskExecutor(store, [handler], options, TimeProvider.System, NullLogger<TodoTaskExecutor>.Instance);
        var dispatcher = new TodoTaskDispatcher(store, options, NullLogger<TodoTaskDispatcher>.Instance);

        var root = await store.CreateRunAsync(Repo, NewTask("Analyze"), null, 3, _cancellationToken);
        var id = Assert.Single(await dispatcher.DispatchAsync(_cancellationToken));
        handler.Next = _ => throw new HttpRequestException("Connection reset.");
        Assert.Equal(TodoTaskStatus.Pending, await executor.ExecuteAsync(id, _cancellationToken));
        Assert.Null(await executor.ExecuteAsync(id, _cancellationToken));

        var retry = (await store.GetRunAsync(Repo, root.Id, _cancellationToken))!.Root;
        Assert.Equal("Connection reset.", retry.Error);
        Assert.InRange(retry.NotBefore!.Value - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));

        var other = await store.CreateRunAsync("owner/other", NewTask("Analyze"), null, 3, _cancellationToken);
        Assert.Equal([other.Id], await dispatcher.DispatchAsync(_cancellationToken));
        handler.Next = _ => throw new DomainRuleException("The repository has no files that can be analyzed.");
        Assert.Equal(TodoTaskStatus.Failed, await executor.ExecuteAsync(other.Id, _cancellationToken));

        var third = await store.CreateRunAsync("owner/third", NewTask("Analyze"), null, 3, _cancellationToken);
        await dispatcher.DispatchAsync(_cancellationToken);
        handler.Next = _ => new TodoTaskOutcome("Planned 1 feature.", [NewTask("Feature")]);
        Assert.Equal(TodoTaskStatus.Waiting, await executor.ExecuteAsync(third.Id, _cancellationToken));
        Assert.Equal(1, (await store.GetRunAsync("owner/third", third.Id, _cancellationToken))!.Total);
    }

    [Fact]
    public void RetryDelay_DoublesWithEveryAttempt()
    {
        var options = new TodoTaskOptions { RetryDelay = TimeSpan.FromMinutes(1) };

        Assert.Equal(
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4)],
            new[] { 1, 2, 3 }.Select(attempts => TodoTaskExecutor.RetryDelay(options, attempts)));
    }

    [Fact]
    public async Task DeleteGeneratedDrafts_DeletesTheRunsDrafts_AndKeepsParentsOfOtherItems()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var items = new WorkItemStore(database.Factory, TimeProvider.System);
        var runId = Guid.NewGuid();
        var epic = Assert.Single(await items.AddGeneratedAsync(Repo, null, WorkItemType.Epic, [new("Quality", "## Goal")], runId, _cancellationToken));
        var features = await items.AddGeneratedAsync(Repo, epic.Id, WorkItemType.Feature, [new("Tests", "Add tests"), new("Docs", "Write docs")], runId, _cancellationToken);
        await items.AddGeneratedAsync(Repo, features[0].Id, WorkItemType.Story, [new("Unit tests", "Cover the store")], runId, _cancellationToken);
        var manual = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Story, ParentId = features[1].Id, Title = "Written by hand" }, _cancellationToken);
        await items.AddGeneratedAsync(Repo, null, WorkItemType.Epic, [new("Other run", "")], Guid.NewGuid(), _cancellationToken);

        Assert.Equal(runId, epic.GeneratedByTaskId);
        var discarded = await items.DeleteGeneratedDraftsAsync(Repo, runId, _cancellationToken);

        Assert.Equal((2, 2), (discarded.Deleted, discarded.Kept));
        var left = await items.GetAllAsync(Repo, cancellationToken: _cancellationToken);
        Assert.Equal(["Docs", "Other run", "Quality", "Written by hand"], left.Select(i => i.Title).Order());
        Assert.Equal(features[1].Id, left.Single(i => i.Id == manual.Id).ParentId);
    }

    [Fact]
    public async Task AddGenerated_RefusesAParentOfTheWrongType()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var items = new WorkItemStore(database.Factory, TimeProvider.System);
        var epic = Assert.Single(await items.AddGeneratedAsync(Repo, null, WorkItemType.Epic, [new("Epic", "")], Guid.NewGuid(), _cancellationToken));

        await Assert.ThrowsAsync<DomainRuleException>(() => items.AddGeneratedAsync(Repo, epic.Id, WorkItemType.Story, [new("Story", "")], Guid.NewGuid(), _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => items.AddGeneratedAsync(Repo, null, WorkItemType.Feature, [new("Feature", "")], Guid.NewGuid(), _cancellationToken));
    }

    private async Task<TodoTask> StartedRunAsync(TodoTaskStore store, int maxAttempts = 3)
    {
        var root = await store.CreateRunAsync(Repo, NewTask("Analyze"), null, maxAttempts, _cancellationToken);
        await store.ClaimDueAsync(10, _cancellationToken);
        return (await store.TryStartAsync(root.Id, Lease, _cancellationToken))!;
    }

    private static NewTodoTask NewTask(string title)
    {
        return new NewTodoTask(TodoTaskKind.AnalyzeRepository, title, "{}");
    }

    private sealed class ScriptedHandler : ITodoTaskHandler
    {
        public Func<TodoTask, TodoTaskOutcome> Next { get; set; } = _ => TodoTaskOutcome.Done(null);

        public TodoTaskKind Kind
        {
            get
            {
                return TodoTaskKind.AnalyzeRepository;
            }
        }

        public Task<TodoTaskOutcome> ExecuteAsync(TodoTask task, CancellationToken cancellationToken)
        {
            return Task.FromResult(Next(task));
        }
    }

    internal sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
        }
    }
}

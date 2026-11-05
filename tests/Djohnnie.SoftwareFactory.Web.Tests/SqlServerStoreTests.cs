using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AuthSessions;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>The store guarantees that only a real SQL Server enforces (see <see cref="SqlServerDatabase"/>).</summary>
public class SqlServerStoreTests
{
    private const string Repo = "owner/repo";
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task BeginAccept_ConcurrentClaimsOfOneDraft_OnlyOneWins()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new WorkItemStore(database.Factory, TimeProvider.System);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var bug = await SaveAsync(store, WorkItemType.Bug, $"Crash {attempt}");
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await store.BeginAcceptAsync(Repo, bug.Id, WorkItemWorkflow.AcceptClaimTimeout, _cancellationToken);
                    return true;
                }
                catch (DomainRuleException)
                {
                    return false;
                }
            })));

            Assert.Equal(1, outcomes.Count(won => won));
        }
    }

    [Fact]
    public async Task BeginAccept_AfterAFailedAttempt_ReturnsWhenThatAttemptStarted()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var time = new ManualTime(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));
        var store = new WorkItemStore(database.Factory, time);
        var bug = await SaveAsync(store, WorkItemType.Bug, "Crash");

        Assert.Null(await store.BeginAcceptAsync(Repo, bug.Id, WorkItemWorkflow.AcceptClaimTimeout, _cancellationToken));
        await store.EndFailedAcceptAsync(Repo, bug.Id, issueMayExist: true, _cancellationToken);
        time.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero),
            await store.BeginAcceptAsync(Repo, bug.Id, WorkItemWorkflow.AcceptClaimTimeout, _cancellationToken));

        // A claim that expired without ending also counts, but the earliest attempt is kept.
        time.Now += WorkItemWorkflow.AcceptClaimTimeout;
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero),
            await store.BeginAcceptAsync(Repo, bug.Id, WorkItemWorkflow.AcceptClaimTimeout, _cancellationToken));

        var accepted = await store.MarkAcceptedAsync(Repo, bug.Id, 1001, 1, "https://github.com/owner/repo/issues/1", _cancellationToken);
        Assert.Equal(WorkItemStatus.Accepted, accepted.Status);
        await Assert.ThrowsAsync<DomainRuleException>(() => store.BeginAcceptAsync(Repo, bug.Id, WorkItemWorkflow.AcceptClaimTimeout, _cancellationToken));
    }

    [Fact]
    public async Task FirstAccessOfARepository_FromConcurrentRequests_AddsOneRow()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new RepositoryStore(database.Factory, TimeProvider.System);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var name = $"Owner/New-Repo-{attempt}";
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.RecordAccessAsync(name, _cancellationToken))));

            await using var db = database.Factory.CreateDbContext();
            Assert.Equal(1, await db.Repositories.CountAsync(r => r.FullName == name.ToLowerInvariant(), _cancellationToken));
        }
    }

    [Fact]
    public async Task DeletingAParentWithChildren_IsRefusedByTheDatabase()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new WorkItemStore(database.Factory, TimeProvider.System);
        var epic = await SaveAsync(store, WorkItemType.Epic, "Passkeys");
        await SaveAsync(store, WorkItemType.Feature, "Passkey login", epic.Id);

        await Assert.ThrowsAsync<DomainRuleException>(() => store.DeleteAsync(Repo, epic.Id, _cancellationToken));

        // Even without the store's check, the restricted foreign key keeps the child's parent.
        await using var db = database.Factory.CreateDbContext();
        db.WorkItems.Remove(await db.WorkItems.SingleAsync(e => e.Id == epic.Id, _cancellationToken));
        await Assert.ThrowsAsync<PersistenceException>(() => db.SaveChangesAsync(_cancellationToken));
    }

    [Fact]
    public async Task AgentFileNames_AreCaseInsensitivePerRepository()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var agents = new AgentStore(database.Factory);

        await agents.SetFavoriteAsync("Owner/Repo", "Reviewer.agent.md", true, _cancellationToken);
        await agents.SetFavoriteAsync("owner/repo", "reviewer.agent.md", false, _cancellationToken);

        await using var db = database.Factory.CreateDbContext();
        var agent = Assert.Single(await db.Agents.ToListAsync(_cancellationToken));
        Assert.False(agent.IsFavorite);
        Assert.Single(await db.Repositories.ToListAsync(_cancellationToken));
    }

    [Fact]
    public async Task AuthSessions_ExpireAndExpiredOnesAreRemoved()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var time = new ManualTime(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));
        var store = new AuthSessionStore(database.Factory, time);

        await store.SaveAsync(new AuthSession("old", [1, 2, 3], time.Now.AddHours(1)), _cancellationToken);
        Assert.Equal([1, 2, 3], (await store.GetAsync("old", _cancellationToken))!.Ticket);

        time.Now = time.Now.AddHours(2);
        Assert.Null(await store.GetAsync("old", _cancellationToken));

        await store.SaveAsync(new AuthSession("new", [4], time.Now.AddHours(8)), _cancellationToken);
        await store.SaveAsync(new AuthSession("new", [5], time.Now.AddHours(8)), _cancellationToken);
        Assert.Equal([5], (await store.GetAsync("new", _cancellationToken))!.Ticket);

        await store.RemoveAsync("new", _cancellationToken);
        await using var db = database.Factory.CreateDbContext();
        Assert.Empty(await db.AuthSessions.ToListAsync(_cancellationToken));
    }

    private Task<WorkItem> SaveAsync(WorkItemStore store, WorkItemType type, string title, int? parentId = null)
    {
        return store.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = type, ParentId = parentId, Title = title, Description = $"About {title}."
        }, _cancellationToken);
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}

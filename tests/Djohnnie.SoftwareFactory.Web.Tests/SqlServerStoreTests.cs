using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AuthSessions;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.InternalModels;
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
    public async Task RecordUsage_FromConcurrentRequests_LosesNoUpdate()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var agents = new InternalAgentStore(database.Factory, TimeProvider.System);
        await new InternalModelStore(database.Factory, TimeProvider.System).SaveAsync(
            new InternalModel { Name = "gpt", InputPricePerMillion = 0.000001m, OutputPricePerMillion = 1m }, _cancellationToken);
        await agents.EnsureAsync([InternalAgentDefaults.Find(InternalAgentKeys.ChatSuggestions)!], _cancellationToken);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            agents.RecordUsageAsync(InternalAgentKeys.ChatSuggestions, "gpt", new ModelUsage(1_000_000, 10), InternalAgentUsageSource.ModelTest, _cancellationToken))));

        var agent = (await agents.GetAsync(InternalAgentKeys.ChatSuggestions, _cancellationToken))!;
        Assert.Equal((12L, 12_000_000L, 120L), (agent.RequestCount, agent.TotalInputTokens, agent.TotalOutputTokens));
        Assert.Equal((0.000012m, 0.00012m), (agent.TotalInputCost, agent.TotalOutputCost));
        await using var db = database.Factory.CreateDbContext();
        Assert.Equal(12, await db.InternalAgentUsages.CountAsync(_cancellationToken));
    }

    [Fact]
    public async Task Migration_TurnsFreeTextModelsIntoLinkedCatalogEntries()
    {
        await using var database = await SqlServerDatabase.CreateAsync("20261109122056_UseGuidPrimaryKeysAndSysId");
        await using (var old = database.Factory.CreateDbContext())
        {
            await old.Database.ExecuteSqlRawAsync("""
                INSERT INTO [InternalAgents] ([Id], [Key], [Name], [Description], [Kind], [Instructions], [Model], [IsEnabled], [CreatedAt], [UpdatedAt])
                VALUES (NEWID(), 'one', 'One', '', 'Copilot', 'P', 'gpt-shared', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
                       (NEWID(), 'two', 'Two', '', 'Copilot', 'P', 'GPT-Shared', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
                       (NEWID(), 'three', 'Three', '', 'Background', 'P', '', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                """, _cancellationToken);
        }

        await using var db = database.Factory.CreateDbContext();
        await db.Database.MigrateAsync(_cancellationToken);

        var model = await db.InternalModels.SingleAsync(_cancellationToken);
        Assert.Equal("gpt-shared", model.Name, ignoreCase: true);
        Assert.Equal(model.Name, model.Label);
        Assert.Equal((0m, 0m, (decimal?)null), (model.InputPricePerMillion, model.OutputPricePerMillion, model.CachedInputPricePerMillion));
        var linked = await db.InternalAgents.OrderBy(a => a.Key).Select(a => a.InternalModelId).ToListAsync(_cancellationToken);
        Assert.Equal([model.Id, null, model.Id], linked);
    }

    [Fact]
    public async Task Migration_KeepsTheInputPriceAsTheCachedPriceOfRecordedRequests()
    {
        await using var database = await SqlServerDatabase.CreateAsync("20261109125325_AddInternalModelsAndUsage");
        await using (var old = database.Factory.CreateDbContext())
        {
            await old.Database.ExecuteSqlRawAsync("""
                DECLARE @agent uniqueidentifier = NEWID();
                INSERT INTO [InternalAgents] ([Id], [Key], [Name], [Description], [Kind], [Instructions], [IsEnabled], [CreatedAt], [UpdatedAt])
                VALUES (@agent, 'one', 'One', '', 'Copilot', 'P', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                INSERT INTO [InternalAgentUsages] ([Id], [InternalAgentId], [Model], [InputTokens], [OutputTokens], [InputPricePerMillion], [OutputPricePerMillion], [InputCost], [OutputCost], [OccurredAt])
                VALUES (NEWID(), @agent, 'gpt', 1, 1, 2.5, 10, 0, 0, SYSDATETIMEOFFSET());
                """, _cancellationToken);
        }

        await using var db = database.Factory.CreateDbContext();
        await db.Database.MigrateAsync(_cancellationToken);

        Assert.Equal(2.5m, (await db.InternalAgentUsages.SingleAsync(_cancellationToken)).CachedInputPricePerMillion);
    }

    [Fact]
    public async Task Migration_MarksRequestsRecordedBeforeSourcesAsUnknown()
    {
        await using var database = await SqlServerDatabase.CreateAsync("20261109133036_AddInternalModelFlags");
        await using (var old = database.Factory.CreateDbContext())
        {
            await old.Database.ExecuteSqlRawAsync("""
                DECLARE @agent uniqueidentifier = NEWID();
                INSERT INTO [InternalAgents] ([Id], [Key], [Name], [Description], [Kind], [Instructions], [IsEnabled], [CreatedAt], [UpdatedAt])
                VALUES (@agent, 'one', 'One', '', 'Copilot', 'P', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                INSERT INTO [InternalAgentUsages] ([Id], [InternalAgentId], [Model], [InputTokens], [OutputTokens], [InputPricePerMillion], [OutputPricePerMillion], [InputCost], [OutputCost], [OccurredAt])
                VALUES (NEWID(), @agent, 'gpt', 1, 1, 2.5, 10, 0, 0, SYSDATETIMEOFFSET());
                """, _cancellationToken);
        }

        await using var db = database.Factory.CreateDbContext();
        await db.Database.MigrateAsync(_cancellationToken);

        Assert.Equal(InternalAgentUsageSource.Unknown, (await db.InternalAgentUsages.SingleAsync(_cancellationToken)).Source);
    }

    [Fact]
    public async Task DeletingAModelInUse_IsRefusedByTheDatabase_AndNamesAreCaseInsensitive()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var models = new InternalModelStore(database.Factory, TimeProvider.System);
        var agents = new InternalAgentStore(database.Factory, TimeProvider.System);
        var model = await models.SaveAsync(new InternalModel { Name = "GPT-Used" }, _cancellationToken);
        await Assert.ThrowsAsync<DomainRuleException>(() => models.SaveAsync(new InternalModel { Name = "gpt-used" }, _cancellationToken));
        var defaults = InternalAgentDefaults.Find(InternalAgentKeys.AgentCopilot)!;
        await agents.EnsureAsync([defaults], _cancellationToken);
        await agents.SaveAsync(defaults with { ModelId = model.Id }, _cancellationToken);

        await Assert.ThrowsAsync<DomainRuleException>(() => models.DeleteAsync(model.Id, _cancellationToken));

        // Even without the store's check, the restricted foreign key keeps the agent's model.
        await using var db = database.Factory.CreateDbContext();
        db.InternalModels.Remove(await db.InternalModels.SingleAsync(m => m.Id == model.Id, _cancellationToken));
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

    [Fact]
    public async Task GetCounts_CountsPerTypeByCurrentStatusAndForOneRepositoryOnly()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new WorkItemStore(database.Factory, TimeProvider.System);
        await SaveAsync(store, WorkItemType.Task, "Draft");
        var accepted = await SaveAsync(store, WorkItemType.Task, "Accepted");
        await store.MarkAcceptedAsync(Repo, accepted.Id, 1, 1, "https://example.com/1", _cancellationToken);
        var done = await SaveAsync(store, WorkItemType.Task, "Done");
        await store.MarkAcceptedAsync(Repo, done.Id, 2, 2, "https://example.com/2", _cancellationToken);
        await store.ApplyIssueAsync(Repo, done.Id, new WorkItemIssueSnapshot(2, "https://example.com/2", "Done", string.Empty, true, false), _cancellationToken);
        await store.SaveAsync(new WorkItem { RepositoryFullName = "owner/other", Type = WorkItemType.Task, Title = "Elsewhere" }, _cancellationToken);

        var counts = await store.GetCountsAsync("OWNER/repo", _cancellationToken);

        Assert.Equal(WorkItemTypeRules.All, counts.Select(c => c.Type));
        Assert.Equal(new WorkItemCounts(WorkItemType.Task, 1, 1, 1), counts.Single(c => c.Type == WorkItemType.Task));
        Assert.Equal(new WorkItemCounts(WorkItemType.Epic, 0, 0, 0), counts.Single(c => c.Type == WorkItemType.Epic));
    }

    private Task<WorkItem> SaveAsync(WorkItemStore store, WorkItemType type, string title, Guid? parentId = null)
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

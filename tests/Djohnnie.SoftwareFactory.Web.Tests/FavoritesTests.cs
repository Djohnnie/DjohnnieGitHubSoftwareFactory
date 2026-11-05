using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Application.Features.AiModels;
using Djohnnie.SoftwareFactory.Application.Features.Epics;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>Favorite repositories, agents and models, and the repository rows everything else links to.</summary>
public class FavoritesTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    public FavoritesTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _services = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(options))
            // Epic handlers need a GitHub client; these tests never call it.
            .AddSingleton<IGitHubTokenProvider, UnusedTokenProvider>()
            .BuildServiceProvider();
    }

    private sealed class UnusedTokenProvider : IGitHubTokenProvider
    {
        public Task<string> GetAccessTokenAsync()
        {
            throw new InvalidOperationException("GitHub must not be called.");
        }
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    private AppDbContext CreateDb()
    {
        return _services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    }

    [Fact]
    public async Task FavoriteRepository_IsListedSeparatelyFromRecentOnes()
    {
        var repositories = _services.GetRequiredService<RepositoryService>();
        await repositories.RecordAccessAsync("owner/one", _cancellationToken);
        await repositories.RecordAccessAsync("owner/two", _cancellationToken);

        await repositories.SetFavoriteAsync("Owner/Two", true, _cancellationToken);
        // A repository that was never opened can be a favorite too.
        await repositories.SetFavoriteAsync("owner/never-opened", true, _cancellationToken);

        Assert.Equal(["owner/never-opened", "owner/two"], (await repositories.GetFavoritesAsync(_cancellationToken)).Select(r => r.FullName));
        Assert.Equal(["owner/one"], (await repositories.GetRecentAsync(_cancellationToken)).Select(r => r.FullName));
        var neverOpened = (await repositories.GetFavoritesAsync(_cancellationToken))[0];
        Assert.Equal("never-opened", neverOpened.Name);
        Assert.Null(neverOpened.LastAccessedAt);

        await repositories.SetFavoriteAsync("owner/two", false, _cancellationToken);

        Assert.Equal(["owner/never-opened"], (await repositories.GetFavoritesAsync(_cancellationToken)).Select(r => r.FullName));
        Assert.Contains("owner/two", (await repositories.GetRecentAsync(_cancellationToken)).Select(r => r.FullName));
        await using var db = CreateDb();
        Assert.Equal(3, await db.Repositories.CountAsync(_cancellationToken));
    }

    [Fact]
    public async Task InvalidRepositoryName_CannotBeMarkedAsFavorite()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _services.GetRequiredService<RepositoryService>().SetFavoriteAsync("not-a-repository", true, _cancellationToken));
    }

    [Fact]
    public async Task Epics_AreLinkedToOneRepositoryRowRegardlessOfCase()
    {
        var epics = _services.GetRequiredService<EpicService>();

        await epics.SaveAsync(new Epic { RepositoryFullName = "Owner/Repo", Title = "First" }, _cancellationToken);
        await epics.SaveAsync(new Epic { RepositoryFullName = "owner/repo", Title = "Second" }, _cancellationToken);

        await using var db = CreateDb();
        var repository = await db.Repositories.SingleAsync(_cancellationToken);
        Assert.Equal("owner/repo", repository.FullName);
        Assert.Null(repository.LastAccessedAt);
        Assert.All(await db.Epics.ToListAsync(_cancellationToken), epic => Assert.Equal(repository.Id, epic.RepositoryId));
        Assert.Equal(["First", "Second"],
            (await epics.GetAllAsync("OWNER/REPO", _cancellationToken)).Select(e => e.Title).Order());
    }

    [Fact]
    public async Task AgentSync_AddsAndRemovesRowsAndKeepsFavorites()
    {
        var agents = _services.GetRequiredService<AgentService>();
        Assert.Empty(await agents.SyncAsync("owner/repo", ["reviewer", "planner"], _cancellationToken));

        await agents.SetFavoriteAsync("owner/repo", "planner", true, _cancellationToken);
        await agents.SetFavoriteAsync("owner/other", "planner", true, _cancellationToken);
        var favorites = await agents.SyncAsync("owner/repo", ["planner", "tester"], _cancellationToken);

        Assert.Equal(["planner"], favorites);
        await using var db = CreateDb();
        var rows = await db.Agents.Include(a => a.Repository).ToListAsync(_cancellationToken);
        Assert.Equal(["owner/other:planner:True", "owner/repo:planner:True", "owner/repo:tester:False"],
            rows.Select(a => $"{a.Repository.FullName}:{a.FileName}:{a.IsFavorite}").Order());
    }

    [Fact]
    public async Task ModelFavorite_IsSavedAndSurvivesEditsAndImports()
    {
        var models = _services.GetRequiredService<AiModelService>();
        var model = await models.SaveAsync(new AiModel { Name = "GPT-5", ModelId = "gpt-5", InputPrice = 1m }, _cancellationToken);

        await models.SetFavoriteAsync(model.Id, true, _cancellationToken);
        var stored = Assert.Single(await models.GetAllAsync(_cancellationToken));
        Assert.True(stored.IsFavorite);

        await models.SaveAsync(stored with { ModelId = "gpt-5-new" }, _cancellationToken);
        await models.ImportAsync([new AiModel { Name = "GPT-5", ModelId = "ignored", InputPrice = 2m }], _cancellationToken);

        stored = Assert.Single(await models.GetAllAsync(_cancellationToken));
        Assert.True(stored.IsFavorite);
        Assert.Equal(2m, stored.InputPrice);
    }
}

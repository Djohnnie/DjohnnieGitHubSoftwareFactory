using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class RepositoryHistoryTests
{
    [Fact]
    public async Task AccessHistory_ReturnsFiveMostRecentDistinctRepositoriesAndRefreshesRevisits()
    {
        var clock = new TestClock();
        using var services = CreateServices(clock);
        var history = services.GetRequiredService<RepositoryHistoryService>();
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var index = 0; index < 7; index++)
        {
            clock.Now = clock.Now.AddMinutes(1);
            await history.RecordAccessAsync($"owner/repo-{index}", cancellationToken);
        }
        clock.Now = clock.Now.AddMinutes(1);
        await history.RecordAccessAsync("Owner/Repo-0", cancellationToken);

        var recent = await history.GetRecentAsync(cancellationToken);

        Assert.Equal(["owner/repo-0", "owner/repo-6", "owner/repo-5", "owner/repo-4", "owner/repo-3"],
            recent.Select(repository => repository.FullName));
        Assert.Equal(clock.Now, recent[0].LastAccessedAt);
        await using var db = services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        Assert.Equal(7, await db.RepositoryAccesses.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task InvalidRepositoryName_IsRejectedWithoutRecordingAccess()
    {
        using var services = CreateServices(new TestClock());
        var history = services.GetRequiredService<RepositoryHistoryService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            history.RecordAccessAsync("not-a-full-repository-name", TestContext.Current.CancellationToken));

        Assert.Empty(await history.GetRecentAsync(TestContext.Current.CancellationToken));
    }

    private static ServiceProvider CreateServices(TimeProvider clock)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton(clock)
            .AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(options))
            .BuildServiceProvider();
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

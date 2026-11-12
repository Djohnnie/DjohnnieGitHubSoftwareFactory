using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.SyncLog;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class SyncLogTests : BunitContext
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;

    public SyncLogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLogging();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddApplication();
    }

    private ISyncLogStore Store => Services.GetRequiredService<ISyncLogStore>();
    private SyncLogService Log => Services.GetRequiredService<SyncLogService>();

    [Fact]
    public async Task GetPage_ShowsTheNewestEventsFirstAndCountsThemAll()
    {
        await Store.AddAsync([Event(0, "first"), Event(1, "second"), Event(2, "third")], _cancellationToken);

        var first = await Log.GetPageAsync(new SyncLogQuery(0, 2), _cancellationToken);
        var second = await Log.GetPageAsync(new SyncLogQuery(1, 2), _cancellationToken);

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(["third", "second"], first.Items.Select(e => e.Message));
        Assert.Equal(["first"], second.Items.Select(e => e.Message));
    }

    [Fact]
    public async Task GetPage_FiltersOnTheLevelAndUpAndOnTheRepository()
    {
        await Store.AddAsync([
            Event(0, "info", SyncLogLevel.Information, "owner/repo"),
            Event(1, "warning", SyncLogLevel.Warning, "owner/other"),
            Event(2, "error", SyncLogLevel.Error, "owner/repo"),
            Event(3, "run", SyncLogLevel.Error)
        ], _cancellationToken);

        var warnings = await Log.GetPageAsync(new SyncLogQuery(0, 10, SyncLogLevel.Warning), _cancellationToken);
        var repo = await Log.GetPageAsync(new SyncLogQuery(0, 10, RepositoryFullName: "owner/repo"), _cancellationToken);

        Assert.Equal(["run", "error", "warning"], warnings.Items.Select(e => e.Message));
        Assert.Equal(["error", "info"], repo.Items.Select(e => e.Message));
        Assert.Equal(["owner/other", "owner/repo"], await Log.GetRepositoriesAsync(_cancellationToken));
    }

    [Fact]
    public async Task AddOrRepeat_OnlyRepeatsTheNewestEventOfTheSameKind()
    {
        await Store.AddOrRepeatAsync(Event(0, "", kind: SyncLogEventKind.NoChanges), n => $"quiet {n}", _cancellationToken);
        await Store.AddOrRepeatAsync(Event(1, "", kind: SyncLogEventKind.NoChanges), n => $"quiet {n}", _cancellationToken);
        await Store.AddOrRepeatAsync(Event(2, "", kind: SyncLogEventKind.NoEligibleWorkItems), n => $"idle {n}", _cancellationToken);
        await Store.AddOrRepeatAsync(Event(3, "", kind: SyncLogEventKind.NoChanges), n => $"quiet {n}", _cancellationToken);

        var page = await Log.GetPageAsync(new SyncLogQuery(0, 10), _cancellationToken);

        Assert.Equal(["quiet 1", "idle 1", "quiet 2"], page.Items.Select(e => e.Message));
        var repeated = page.Items[2];
        Assert.Equal((Start, Start.AddMinutes(1)), (repeated.OccurredAt, repeated.LastOccurredAt));
    }

    [Fact]
    public async Task Add_CutsOffMessagesThatAreTooLong()
    {
        await Store.AddAsync([Event(0, new string('x', SyncLogEvent.MaxMessageLength + 10))], _cancellationToken);

        var stored = Assert.Single((await Log.GetPageAsync(new SyncLogQuery(0, 10), _cancellationToken)).Items);

        Assert.Equal(SyncLogEvent.MaxMessageLength, stored.Message.Length);
        Assert.EndsWith("…", stored.Message);
    }

    [Theory]
    [InlineData(-1, 25, null)]
    [InlineData(0, 0, null)]
    [InlineData(0, SyncLogQuery.MaxPageSize + 1, null)]
    [InlineData(0, 25, "owner/repo/../../user")]
    public async Task GetPage_RefusesAnInvalidQuery(int page, int pageSize, string? repository)
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => Log.GetPageAsync(new SyncLogQuery(page, pageSize, RepositoryFullName: repository), _cancellationToken));
    }

    [Fact]
    public async Task Page_ShowsTheEventsNewestFirstWithTheirIssueAndRepeatedRuns()
    {
        await Store.AddAsync([
            Event(0, "Epic 'Passkeys': title 'Login' → 'Passkeys'.", repository: "owner/repo", issueNumber: 42, kind: SyncLogEventKind.WorkItemChanged),
            Event(1, "Could not be synced.", SyncLogLevel.Error, "owner/repo", 43, SyncLogEventKind.WorkItemFailed, detail: "System.Text.Json.JsonException: boom")
        ], _cancellationToken);
        await Store.AddOrRepeatAsync(Event(2, "", kind: SyncLogEventKind.NoChanges), n => $"No changes for {n} iterations.", _cancellationToken);
        await Store.AddOrRepeatAsync(Event(3, "", kind: SyncLogEventKind.NoChanges), n => $"No changes for {n} iterations.", _cancellationToken);
        Render<MudPopoverProvider>();

        var cut = Render<Components.Pages.SyncLog>();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));
        var rows = cut.FindAll("tbody tr");
        Assert.Contains("No changes for 2 iterations.", rows[0].TextContent);
        Assert.Contains("since", rows[0].QuerySelector("td[data-label='When']")!.TextContent);
        Assert.Contains("Error", rows[1].QuerySelector("td[data-label='Level']")!.TextContent);
        Assert.Contains("JsonException", rows[1].QuerySelector("details pre")!.TextContent);
        var issue = rows[2].QuerySelector("td[data-label='Issue'] a")!;
        Assert.Equal(("#42", "https://github.com/owner/repo/issues/42"), (issue.TextContent.Trim(), issue.GetAttribute("href")));
        Assert.DoesNotContain("since", rows[2].TextContent);
    }

    [Fact]
    public void Page_WithoutEvents_SaysTheSyncHasntRunYet()
    {
        Render<MudPopoverProvider>();

        var cut = Render<Components.Pages.SyncLog>();

        cut.WaitForAssertion(() => Assert.Contains("Nothing logged yet", cut.Markup));
    }

    private static SyncLogEvent Event(int minute, string message, SyncLogLevel level = SyncLogLevel.Information, string? repository = null,
        int? issueNumber = null, SyncLogEventKind kind = SyncLogEventKind.RunCompleted, string? detail = null)
    {
        var at = Start.AddMinutes(minute);
        return new(Guid.NewGuid(), Guid.NewGuid(), at, at, 1, level, kind, repository, issueNumber, null, message, detail);
    }
}

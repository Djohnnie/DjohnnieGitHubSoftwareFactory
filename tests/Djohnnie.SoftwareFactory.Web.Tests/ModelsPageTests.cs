using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.AiModels;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class ModelsPageTests : BunitContext, IAsyncLifetime
{
    private readonly ModelPricingScraperTests.TestDbFactory _dbFactory;

    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ModelsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddDataProtection();
        Services.AddLogging();
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<RepositoryContext>();
        _dbFactory = new ModelPricingScraperTests.TestDbFactory(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(_dbFactory);
        Services.AddSingleton(TimeProvider.System);
        Services.AddApplication();
        Services.AddSingleton(new ModelPricingScraper(new HttpClient(new FakePricingHandler())));
    }

    [Fact]
    public async Task Fetch_ListsModelsNotOnThePageAndRemovesTheSelectedOnes()
    {
        var service = Services.GetRequiredService<AiModelService>();
        var ct = Xunit.TestContext.Current.CancellationToken;
        await service.SaveAsync(new AiModel { Name = "GPT-5.3-Codex", ModelId = "gpt-5.3-codex", Provider = "OpenAI", InputPrice = 1m, OutputPrice = 2m }, ct);
        await service.SaveAsync(new AiModel { Name = "Retired Model", ModelId = "retired-model", Provider = "OpenAI", SourceUrl = "https://docs.github.com/x" }, ct);
        await service.SaveAsync(new AiModel { Name = "My Custom Model", ModelId = "my-custom-model", Provider = "Custom" }, ct);
        var provider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var cut = Render<Models>();
        cut.WaitForAssertion(() => Assert.Contains("retired-model", cut.Markup));

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Import from GitHub docs")).Click());
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Fetch").Click());

        cut.WaitForAssertion(() => Assert.Contains("Not found on the page", cut.Markup), TimeSpan.FromSeconds(2));
        var missingRows = cut.FindAll(".missing-models tbody tr");
        Assert.Equal(2, missingRows.Count);
        Assert.Contains("Added manually", cut.Find(".missing-models").TextContent);
        Assert.DoesNotContain("GPT-5.3-Codex", cut.Find(".missing-models").TextContent);

        var retiredRow = missingRows.Single(r => r.TextContent.Contains("Retired Model"));
        await cut.InvokeAsync(() => retiredRow.QuerySelector("input[type=checkbox]")!.Change(true));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Remove selected")).Click());
        provider.WaitForElement(".mud-dialog");
        await provider.InvokeAsync(() => provider.FindAll(".mud-dialog button").Single(b => b.TextContent.Trim() == "Remove").Click());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".missing-models tbody tr")), TimeSpan.FromSeconds(2));
        var names = (await service.GetAllAsync(ct)).Select(m => m.Name).Order().ToList();
        Assert.Equal(["GPT-5.3-Codex", "My Custom Model"], names);
    }

    private sealed class FakePricingHandler : HttpMessageHandler
    {
        private const string Html = """
            <html><body>
            <h3 id="openai">OpenAI</h3>
            <table aria-labelledby="openai"><thead><tr><th>Model</th><th>Input</th><th>Output</th></tr></thead>
            <tbody><tr><td>GPT-5.3-Codex</td><td>$1.75</td><td>$14.00</td></tr></tbody></table>
            </body></html>
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(Html) });
        }
    }

    [Fact]
    public async Task NewModel_IsSavedFromItsPage_AndTheAddressMovesToTheSavedModel()
    {
        var service = Services.GetRequiredService<AiModelService>();
        var ct = Xunit.TestContext.Current.CancellationToken;
        Render<MudPopoverProvider>();
        var cut = Render<AiModelEditor>();
        cut.WaitForElement("input");
        Assert.DoesNotContain("Reset", cut.FindAll("button").Select(b => b.TextContent.Trim()));

        cut.FindAll("input")[0].Input("Claude Sonnet 4.6");
        cut.WaitForAssertion(() => Assert.Equal("claude-sonnet-4.6", cut.FindAll("input")[1].GetAttribute("value")));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click());

        var saved = Assert.Single(await service.GetAllAsync(ct));
        Assert.Equal(("Claude Sonnet 4.6", "claude-sonnet-4.6"), (saved.Name, saved.ModelId));
        cut.WaitForAssertion(() => Assert.EndsWith($"/models/{saved.Id}", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public async Task Grid_ShowsWhatAgentSessionsSpentPerModel_AndTheirTotal_AndLinksToTheModelsPage()
    {
        var ct = Xunit.TestContext.Current.CancellationToken;
        var service = Services.GetRequiredService<AiModelService>();
        var used = await service.SaveAsync(new AiModel { Name = "Grok 4.7", ModelId = "grok-4.7", Provider = "xAI", InputPrice = 2m, OutputPrice = 6m }, ct);
        await service.SaveAsync(new AiModel { Name = "GPT-5.3-Codex", ModelId = "gpt-5.3-codex", Provider = "OpenAI" }, ct);
        await SeedSessionAsync(used.Id, 1_250_000_000, ct);
        await SeedSessionAsync(used.Id, 250_000_000, ct);

        Render<MudPopoverProvider>();
        var cut = Render<Models>();
        cut.WaitForAssertion(() => Assert.Contains("grok-4.7", cut.Markup));

        var usage = cut.FindAll("td.model-usage").Select(td => td.TextContent.Trim()).ToList();
        Assert.Contains(usage, text => text.Contains("1.5 AI credits") && text.Contains("2 sessions"));
        Assert.Contains("–", usage);
        Assert.Equal("1.5 AI credits", cut.Find(".models-usage-total").TextContent.Trim());
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == $"/models/{used.Id}");
    }

    [Fact]
    public async Task ModelPage_ShowsTheRunningTotals_AndTheSessions_AndSavingKeepsThem()
    {
        var ct = Xunit.TestContext.Current.CancellationToken;
        var service = Services.GetRequiredService<AiModelService>();
        var model = await service.SaveAsync(new AiModel { Name = "Grok 4.7", ModelId = "grok-4.7", Provider = "xAI", InputPrice = 2m, OutputPrice = 6m }, ct);
        await SeedSessionAsync(model.Id, 2_000_000_000, ct);

        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        var cut = Render<AiModelEditor>(p => p.Add(e => e.Id, model.Id));
        cut.WaitForAssertion(() => Assert.Contains("2 AI credits", cut.Find("[aria-label=Usage]").TextContent));
        cut.WaitForAssertion(() => Assert.Contains("Add the endpoint", cut.Find("[aria-label='Agent sessions']").TextContent));
        Assert.Contains("owner/repo", cut.Find("[aria-label='Agent sessions']").TextContent);

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click());
        var stored = (await service.GetAsync(model.Id, ct))!;
        Assert.Equal((1, 2_000_000_000L), (stored.Usage.Sessions, stored.Usage.NanoAiu));
    }

    private static DataAccess.Sql.Entities.Repository NewRepository()
    {
        var repository = DataAccess.Sql.Entities.Repository.Create("owner/repo");
        repository.Id = Guid.NewGuid();
        return repository;
    }

    /// <summary>One agent worker session on a work item, counted on <paramref name="modelId"/> as the store records it.</summary>
    private async Task SeedSessionAsync(Guid modelId, long nanoAiu, CancellationToken ct)
    {
        await using var db = _dbFactory.CreateDbContext();
        var repository = await db.Repositories.SingleOrDefaultAsync(r => r.FullName == "owner/repo", ct)
            ?? db.Repositories.Add(NewRepository()).Entity;
        var item = new DataAccess.Sql.Entities.WorkItem { Id = Guid.NewGuid(), Repository = repository, Title = "Add the endpoint", Description = string.Empty };
        db.WorkItems.Add(item);
        var run = new DataAccess.Sql.Entities.AgentRun { Id = Guid.NewGuid(), WorkItemId = item.Id, RepositoryFullName = "owner/repo", Attempts = 1 };
        db.AgentRuns.Add(run);
        db.AgentRunModelUsages.Add(new DataAccess.Sql.Entities.AgentRunModelUsage
        {
            Id = Guid.NewGuid(), AgentRunId = run.Id, AiModelId = modelId, Attempt = 1, Model = "grok-4.7", NanoAiu = nanoAiu, Complete = true,
            Requests = 4, InputTokens = 1000, OutputTokens = 200, OccurredAt = DateTimeOffset.UtcNow
        });
        var model = await db.AiModels.SingleAsync(m => m.Id == modelId, ct);
        model.UsageSessions++;
        model.UsageNanoAiu += nanoAiu;
        model.UsageRequests += 4;
        model.UsageInputTokens += 1000;
        model.UsageOutputTokens += 200;
        await db.SaveChangesAsync(ct);
    }
}

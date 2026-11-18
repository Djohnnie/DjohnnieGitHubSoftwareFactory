using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.AiModels;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class ModelsPageTests : BunitContext, IAsyncLifetime
{
    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ModelsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
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
    public async Task NewModel_IsSavedFromDialogAndListedWithPrices()
    {
        await Services.GetRequiredService<AiModelService>().SaveAsync(new AiModel
        {
            Name = "Grok 4.7",
            ModelId = "grok-4.7",
            Provider = "xAI",
            InputPrice = 2m,
            OutputPrice = 6m
        }, Xunit.TestContext.Current.CancellationToken);
        var provider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var cut = Render<Models>();
        cut.WaitForAssertion(() => Assert.Contains("grok-4.7", cut.Markup));
        Assert.Contains("6 AI credits", cut.Markup);

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("New model")).Click());
        provider.WaitForElement(".mud-dialog input");
        provider.FindAll(".mud-dialog input")[0].Input("Claude Sonnet 4.6");
        provider.WaitForAssertion(() => Assert.Equal("claude-sonnet-4.6", provider.FindAll(".mud-dialog input")[1].GetAttribute("value")));
        await provider.InvokeAsync(() => provider.FindAll(".mud-dialog button").Single(b => b.TextContent.Trim() == "Save").Click());

        cut.WaitForAssertion(() => Assert.Contains("claude-sonnet-4.6", cut.Markup), TimeSpan.FromSeconds(2));
        Assert.Equal(2, (await Services.GetRequiredService<AiModelService>().GetAllAsync(Xunit.TestContext.Current.CancellationToken)).Count);
    }
}

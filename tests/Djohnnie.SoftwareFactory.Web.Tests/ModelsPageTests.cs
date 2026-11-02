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
    ValueTask IAsyncLifetime.InitializeAsync() => ValueTask.CompletedTask;

    public ModelsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton(TimeProvider.System);
        Services.AddApplication();
        Services.AddSingleton(new ModelPricingScraper(new HttpClient()));
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
        Assert.Contains("$6.00", cut.Markup);

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("New model")).Click());
        provider.WaitForElement(".mud-dialog input");
        provider.FindAll(".mud-dialog input")[0].Input("Claude Sonnet 4.6");
        provider.WaitForAssertion(() => Assert.Equal("claude-sonnet-4.6", provider.FindAll(".mud-dialog input")[1].GetAttribute("value")));
        await provider.InvokeAsync(() => provider.FindAll(".mud-dialog button").Single(b => b.TextContent.Trim() == "Save").Click());

        cut.WaitForAssertion(() => Assert.Contains("claude-sonnet-4.6", cut.Markup), TimeSpan.FromSeconds(2));
        Assert.Equal(2, (await Services.GetRequiredService<AiModelService>().GetAllAsync(Xunit.TestContext.Current.CancellationToken)).Count);
    }
}

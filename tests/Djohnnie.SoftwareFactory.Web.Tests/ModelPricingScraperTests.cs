using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.AiModels;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class ModelPricingScraperTests
{
    private const string Html = """
        <html><body>
        <h3 id="openai"><a class="heading-link" href="#openai">OpenAI<span aria-hidden="true"></span></a></h3>
        <p>Some note</p>
        <table aria-labelledby="openai"><thead><tr><th>Model</th><th>Release status</th><th>Category</th><th>Tier</th><th>Threshold (input tokens)</th><th>Input</th><th>Cached input</th><th>Cache write</th><th>Output</th></tr></thead>
        <tbody>
        <tr><td>GPT-5.3-Codex</td><td>GA</td><td>Powerful</td><td>Default</td><td>Not applicable</td><td>$1.75</td><td>$0.175</td><td>Not applicable</td><td>$14.00</td></tr>
        <tr><td>GPT-5.6 Luna</td><td>GA</td><td>Lightweight</td><td>Long context</td><td>&gt; 200K</td><td>$0.40</td><td>$0.04</td><td>$0.50</td><td>$1.80</td></tr>
        </tbody></table>
        <h3 id="google"><a href="#google">Google</a></h3>
        <table aria-labelledby="google"><thead><tr><th>Model</th><th>Release status</th><th>Category</th><th>Input</th><th>Cached input</th><th>Output</th></tr></thead>
        <tbody>
        <tr><td>Gemini 3.8 Flash<sup><a href="#fn-1">1</a></sup></td><td>Public preview</td><td>Versatile</td><td>$0.30</td><td>$0.03</td><td>$2.50</td></tr>
        </tbody></table>
        <h3 id="image-generation-models">Image generation models</h3>
        <table aria-labelledby="image-generation-models"><thead><tr><th>Model</th><th>Release status</th><th>Text input</th><th>Image output</th></tr></thead>
        <tbody><tr><td>Image Model</td><td>GA</td><td>$5.00</td><td>$40.00</td></tr></tbody></table>
        </body></html>
        """;

    [Fact]
    public void Parse_ReadsPricingTablesByHeaderAndSkipsOtherTables()
    {
        var models = ModelPricingScraper.Parse(Html, "https://docs.github.com/x");

        Assert.Equal(["GPT-5.3-Codex", "GPT-5.6 Luna", "Gemini 3.8 Flash"], models.Select(m => m.Name));

        var codex = models[0];
        Assert.Equal(("OpenAI", "gpt-5.3-codex", "GA", "Powerful", AiModel.DefaultTier), (codex.Provider, codex.ModelId, codex.ReleaseStatus, codex.Category, codex.Tier));
        Assert.Null(codex.Threshold);
        Assert.Equal((1.75m, 0.175m, (decimal?)null, 14.00m), (codex.InputPrice, codex.CachedInputPrice, codex.CacheWritePrice, codex.OutputPrice));

        var luna = models[1];
        Assert.Equal(("Long context", "> 200K", 0.50m), (luna.Tier, luna.Threshold, luna.CacheWritePrice));

        var gemini = models[2];
        Assert.Equal(("Google", "gemini-3.8-flash", "Public preview", AiModel.DefaultTier, 2.50m), (gemini.Provider, gemini.ModelId, gemini.ReleaseStatus, gemini.Tier, gemini.OutputPrice));
        Assert.Null(gemini.CacheWritePrice);
    }

    [Theory]
    [InlineData("Claude Sonnet 4.6", "claude-sonnet-4.6")]
    [InlineData("GPT-5.4 nano", "gpt-5.4-nano")]
    [InlineData("Claude Opus 4.8 (fast mode) (preview)", "claude-opus-4.8-fast")]
    [InlineData("MAI-Code-1.1-Flash", "mai-code-1.1-flash")]
    public void ToModelId_DerivesAgentModelValue(string name, string expected)
    {
        Assert.Equal(expected, ModelPricingScraper.ToModelId(name));
    }

    [Theory]
    [InlineData("https://example.com/models")]
    [InlineData("http://docs.github.com/en/copilot")]
    [InlineData("not a url")]
    public void ValidateUrl_RejectsNonGitHubDocsUrls(string url)
    {
        Assert.Throws<ArgumentException>(() => ModelPricingScraper.ValidateUrl(url));
    }

    [Fact]
    public async Task Import_AddsNewAndRefreshesPricesButKeepsUserChanges()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton<IDbContextFactory<AppDbContext>>(new TestDbFactory(options))
            .BuildServiceProvider();
        var service = services.GetRequiredService<AiModelService>();
        await service.SaveAsync(new AiModel { Name = "GPT-5.3-Codex", ModelId = "my-codex", IsEnabled = false, InputPrice = 1m, OutputPrice = 10m }, TestContext.Current.CancellationToken);

        var result = await service.ImportAsync(ModelPricingScraper.Parse(Html), TestContext.Current.CancellationToken);
        var again = await service.ImportAsync(ModelPricingScraper.Parse(Html), TestContext.Current.CancellationToken);

        Assert.Equal(new ModelImportResult(2, 1, 0), result);
        Assert.Equal(new ModelImportResult(0, 0, 3), again);
        var codex = (await service.GetAllAsync(TestContext.Current.CancellationToken)).Single(m => m.Name == "GPT-5.3-Codex");
        Assert.Equal(("my-codex", false, 1.75m, 14.00m), (codex.ModelId, codex.IsEnabled, codex.InputPrice, codex.OutputPrice));
    }

    internal sealed class TestDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            return new(options);
        }
    }
}

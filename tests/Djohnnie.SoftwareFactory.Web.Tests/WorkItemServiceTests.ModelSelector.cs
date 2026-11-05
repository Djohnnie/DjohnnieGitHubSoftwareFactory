using System.Text.Json;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.AiModels;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    [Fact]
    public async Task Accept_WithoutModel_KeepsCopilotsAutoAndDoesNotAskTheModelSelector()
    {
        var chat = new ScriptedChatClient();
        await using var services = await ServicesWithChatAsync(chat);
        var items = services.GetRequiredService<WorkItemService>();
        var task = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Bump timeout", Description = "Set the timeout to 30s." }, _cancellationToken);

        var result = await items.AcceptAsync(Repo, task.Id, _cancellationToken);

        Assert.Equal(WorkItemStatus.InProgress, result.WorkItem.Status);
        Assert.Null(result.WorkItem.Model);
        var assign = Assert.Single(_gitHub.Requests, r => r.Path.EndsWith("/assignees"));
        Assert.False(JsonDocument.Parse(assign.Body!).RootElement.GetProperty("agent_assignment").TryGetProperty("model", out _));
        Assert.Empty(chat.Requests);
    }

    [Fact]
    public async Task SelectModel_SuggestsAnEnabledModelByComplexity_WithoutSavingIt()
    {
        var chat = new ScriptedChatClient(ScriptedChatClient.Text(
            """{"modelId":"GPT-5 mini","complexity":"Low","reason":"A one-line config change."}"""));
        await using var services = await ServicesWithChatAsync(chat);
        var items = services.GetRequiredService<WorkItemService>();
        var bug = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Bug", Description = "Set the timeout to 30s." }, _cancellationToken);

        var selection = await items.SelectModelAsync(bug, _cancellationToken);

        Assert.Equal(new ModelSelection("gpt-5-mini", "low", "A one-line config change."), selection);
        Assert.Null((await items.GetAsync(Repo, bug.Id, _cancellationToken))!.Model);
        var input = string.Join("\n", Assert.Single(chat.Requests).Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        Assert.Contains("Set the timeout to 30s.", input);
        Assert.Contains("\"id\": \"gpt-5\"", input);
        Assert.DoesNotContain("disabled-model", input);
        Assert.Contains("You are the Model Selector", InternalAgentTests.SystemPrompt(chat, 0));
        await Assert.ThrowsAsync<ValidationFailedException>(() => items.SelectModelAsync(bug with { Type = WorkItemType.Epic }, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => items.SelectModelAsync(bug with { Description = " " }, _cancellationToken));
    }

    [Theory]
    [InlineData("unknown-model", true)]
    [InlineData("gpt-5", false)]
    public async Task SelectModel_WhenDisabledOrAnUnknownModelIsPicked_SuggestsNothing(string pickedModel, bool enabled)
    {
        var chat = new ScriptedChatClient(ScriptedChatClient.Text($$"""{"modelId":"{{pickedModel}}","complexity":"high","reason":"Hard."}"""));
        await using var services = await ServicesWithChatAsync(chat);
        if (!enabled)
        {
            await InternalAgentTests.DisableAsync(services, InternalAgentKeys.ModelSelector);
        }
        var bug = new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Bug", Description = "Do it." };

        Assert.Null(await services.GetRequiredService<WorkItemService>().SelectModelAsync(bug, _cancellationToken));
        Assert.Equal(enabled ? 1 : 0, chat.Requests.Count);
    }

    private async Task<ServiceProvider> ServicesWithChatAsync(ScriptedChatClient chat)
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var client = new GitHubApiClient(new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") }, new GitHubTokenProvider(new TokenAuth()));
        var services = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions))
            .AddSingleton(new AssistantAgentFactory(chat, NullLoggerFactory.Instance))
            .AddSingleton(client).BuildServiceProvider();
        var models = services.GetRequiredService<AiModelService>();
        await models.SaveAsync(new AiModel { Name = "GPT-5", ModelId = "gpt-5", Category = "Powerful", InputPrice = 1.25m, OutputPrice = 10m }, _cancellationToken);
        await models.SaveAsync(new AiModel { Name = "GPT-5 mini", ModelId = "gpt-5-mini", Category = "Versatile", InputPrice = 0.25m, OutputPrice = 2m }, _cancellationToken);
        await models.SaveAsync(new AiModel { Name = "Disabled", ModelId = "disabled-model", IsEnabled = false }, _cancellationToken);
        return services;
    }
}

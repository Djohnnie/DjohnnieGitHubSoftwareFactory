using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using WorkItem = Djohnnie.SoftwareFactory.Contracts.WorkItems.WorkItem;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class ChatHistoryTests : BunitContext, IAsyncLifetime
{
    private const string Repo = "owner/repo";
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;
    private ScriptedChatClient? _chatClient;
    private bool _popoverRendered;

    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ChatHistoryTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddDataProtection();
        Services.AddLogging();
        AddAuthorization().SetAuthorized("Djohnnie").SetClaims(new Claim(GitHubClaims.AccessToken, "token"));
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<RepositoryContext>();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton(TimeProvider.System);
        Services.AddApplication();
        Services.AddSingleton(_ => new AssistantAgentFactory(_chatClient, NullLoggerFactory.Instance));
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(new NotFoundHandler()) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    private WorkItemService Items => Services.GetRequiredService<WorkItemService>();
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();
    private IChatConversationStore Store => Services.GetRequiredService<IChatConversationStore>();

    [Fact]
    public async Task ReopenedItem_ShowsTheConversation_AndReplaysItIncludingToolCalls()
    {
        var epic = await PrepareEpicAsync();
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("get_selected_template", new Dictionary<string, object?>()),
            ScriptedChatClient.Text("First reply."),
            ScriptedChatClient.Text("Second reply."));

        var first = RenderEpic(epic);
        await SendAsync(first, "First question");
        first.WaitForAssertion(() => Assert.Contains("First reply.", first.Markup), TimeSpan.FromSeconds(5));
        await WaitForStoredAsync(epic, 2);

        var reopened = RenderEpic(epic);
        reopened.WaitForAssertion(() => Assert.Contains("First reply.", reopened.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains("First question", reopened.Markup);

        await SendAsync(reopened, "Second question");
        reopened.WaitForAssertion(() => Assert.Contains("Second reply.", reopened.Markup), TimeSpan.FromSeconds(5));

        var replayed = _chatClient.Requests[^1];
        Assert.Contains(replayed, m => m.Role == ChatRole.User && m.Text.EndsWith("First question", StringComparison.Ordinal));
        Assert.Contains(replayed.SelectMany(m => m.Contents), c => c is FunctionCallContent { Name: "get_selected_template" });
        Assert.Contains(replayed.SelectMany(m => m.Contents), c => c is FunctionResultContent);
        Assert.Contains(replayed, m => m.Role == ChatRole.Assistant && m.Text == "First reply.");
        Assert.Single(replayed, m => m.Text == "First reply.");
    }

    [Fact]
    public async Task StoredDisplay_ShowsWhatTheUserTyped_NotTheContextSentToTheModel()
    {
        var epic = await PrepareEpicAsync();
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("Reply."));

        var cut = RenderEpic(epic);
        await SendAsync(cut, "My question");
        cut.WaitForAssertion(() => Assert.Contains("Reply.", cut.Markup), TimeSpan.FromSeconds(5));
        await WaitForStoredAsync(epic, 2);

        var display = await Store.GetDisplayMessagesAsync(Owner(epic), _cancellationToken);
        Assert.Equal(["My question", "Reply."], display.Select(m => m.Text));
        Assert.Contains("The user's message:", (await Store.GetPayloadsAsync(Owner(epic), _cancellationToken))[0]);
    }

    [Fact]
    public async Task ConversationOfANewItem_IsStoredOnFirstSave()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("Before saving."));
        Navigation.NavigateTo("/epics/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        await SendAsync(cut, "Draft it");
        cut.WaitForAssertion(() => Assert.Contains("Before saving.", cut.Markup), TimeSpan.FromSeconds(5));
        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Saved epic");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        var epic = Assert.Single(await Items.GetAllAsync(Repo, cancellationToken: _cancellationToken));
        Assert.Equal(["Draft it", "Before saving."], (await Store.GetDisplayMessagesAsync(Owner(epic), _cancellationToken)).Select(m => m.Text));
    }

    [Fact]
    public async Task StartNewConversation_ClosesTheOldOne_AndTheModelStartsFresh()
    {
        var epic = await PrepareEpicAsync();
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("Old reply."), ScriptedChatClient.Text("New reply."));
        var cut = RenderEpic(epic);
        await SendAsync(cut, "Old question");
        cut.WaitForAssertion(() => Assert.Contains("Old reply.", cut.Markup), TimeSpan.FromSeconds(5));
        await WaitForStoredAsync(epic, 2);

        cut.WaitForAssertion(() => Assert.False(cut.FindAll("button").Single(b => b.TextContent.Contains("Start new conversation")).HasAttribute("disabled")), TimeSpan.FromSeconds(5));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Start new conversation")).Click());

        cut.WaitForAssertion(() => Assert.DoesNotContain("Old reply.", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Empty(await Store.GetDisplayMessagesAsync(Owner(epic), _cancellationToken));

        await SendAsync(cut, "New question");
        cut.WaitForAssertion(() => Assert.Contains("New reply.", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(_chatClient.Requests[^1], m => m.Text.Contains("Old question", StringComparison.Ordinal));

        await using var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken);
        var conversations = await db.ChatConversations.Include(c => c.Messages).OrderBy(c => c.Id).ToListAsync(_cancellationToken);
        Assert.Equal(2, conversations.Count);
        Assert.NotNull(conversations[0].ClosedAt);
        Assert.NotEmpty(conversations[0].Messages);
        Assert.Null(conversations[1].ClosedAt);
    }

    [Fact]
    public async Task FailedTurn_StaysVisible_ButIsNotReplayed()
    {
        var epic = await PrepareEpicAsync();
        _chatClient = new ScriptedChatClient();
        var cut = RenderEpic(epic);

        await SendAsync(cut, "This fails");
        cut.WaitForAssertion(() => Assert.Contains("Something went wrong", cut.Markup), TimeSpan.FromSeconds(5));
        await WaitForStoredAsync(epic, 2);

        var reopened = RenderEpic(epic);
        reopened.WaitForAssertion(() => Assert.Contains("is-error", reopened.Markup), TimeSpan.FromSeconds(5));
        _chatClient.Enqueue(ScriptedChatClient.Text("Works now."));
        await SendAsync(reopened, "Try again");
        reopened.WaitForAssertion(() => Assert.Contains("Works now.", reopened.Markup), TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(_chatClient.Requests[^1], m => m.Text.Contains("This fails", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryKindOfOwner_KeepsItsOwnConversation()
    {
        var item = await Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Epic", Description = "D" }, _cancellationToken);
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            db.WorkItemTemplates.Add(new() { Id = 11, Type = WorkItemType.Bug, Name = "T", TitleTemplate = "T", Description = "D" });
            db.AgentTemplates.Add(new() { Id = 12, FileName = "t", Name = "T", Content = "C" });
            await db.SaveChangesAsync(_cancellationToken);
        }

        ChatOwner[] owners =
        [
            ChatOwner.ForWorkItem(Repo, item.Id), ChatOwner.ForWorkItemTemplate(11), ChatOwner.ForAgentTemplate(12),
            ChatOwner.ForAgent(Repo, "reviewer.agent.md"), ChatOwner.ForCopilotInstructions(Repo)
        ];
        foreach (var owner in owners)
        {
            await Store.AppendAsync(owner, "key", [new StoredChatMessage(ChatMessageRole.User, owner.Kind.ToString(), "{}")], _cancellationToken);
        }

        foreach (var owner in owners)
        {
            Assert.Equal(owner.Kind.ToString(), Assert.Single(await Store.GetDisplayMessagesAsync(owner, _cancellationToken)).Text);
        }

        Assert.Single(await Store.GetDisplayMessagesAsync(ChatOwner.ForAgent("OWNER/REPO", "reviewer"), _cancellationToken));
        Assert.Empty(await Store.GetDisplayMessagesAsync(ChatOwner.ForWorkItem("other/repo", item.Id), _cancellationToken));
        Assert.Empty(await Store.GetDisplayMessagesAsync(ChatOwner.ForCopilotInstructions("other/repo"), _cancellationToken));

        await Store.AppendAsync(owners[0], "key", [new StoredChatMessage(ChatMessageRole.Assistant, "next", null)], _cancellationToken);
        Assert.Equal(["WorkItem", "next"], (await Store.GetDisplayMessagesAsync(owners[0], _cancellationToken)).Select(m => m.Text));
    }

    [Fact]
    public async Task ResponsesApi_ResumedConversationSendsItsHistory_AndNeverAPreviousResponseId()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var requests = new List<JsonElement>();
        app.MapPost("/openai/v1/responses", async (HttpContext context) =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            requests.Add(body.RootElement.Clone());
            var item = new { type = "message", id = "msg_test", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = "Hello.", annotations = Array.Empty<object>() } } };
            var response = new { id = $"resp_{requests.Count}", @object = "response", created_at = 1, status = "completed", model = "m", output = new[] { item } };
            context.Response.ContentType = "text/event-stream";
            async Task SendEventAsync(string type, object data)
            {
                await context.Response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(data)}\n\n", context.RequestAborted);
            }

            await SendEventAsync("response.created", new { type = "response.created", sequence_number = 0, response });
            await SendEventAsync("response.output_text.delta", new { type = "response.output_text.delta", sequence_number = 1, item_id = "msg_test", output_index = 0, content_index = 0, delta = "Hello." });
            await SendEventAsync("response.completed", new { type = "response.completed", sequence_number = 2, response });
        });
        await app.StartAsync(timeout.Token);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = AssistantAgentFactory.CreateChatClient(new OpenAIOptions { Endpoint = $"{address}/openai/v1/", Model = "m", ApiKey = "key" });
            var factory = new AssistantAgentFactory(client, NullLoggerFactory.Instance);

            var first = factory.CreateConversation("Copilot", "Be brief.");
            await foreach (var _ in first.RunStreamingAsync("Question one", timeout.Token))
            {
            }
            var stored = first.TakeNewMessages("Question one", "Hello.");
            Assert.Equal(2, stored.Count);

            var resumed = factory.CreateConversation("Copilot", "Be brief.");
            await resumed.RestoreAsync(stored.Select(m => m.Payload!).ToList(), timeout.Token);
            await foreach (var _ in resumed.RunStreamingAsync("Question two", timeout.Token))
            {
            }

            var last = requests[^1];
            Assert.False(last.TryGetProperty("previous_response_id", out var previous) && previous.ValueKind != JsonValueKind.Null);
            var input = last.GetProperty("input").GetRawText();
            Assert.Contains("Question one", input);
            Assert.Contains("Hello.", input);
            Assert.Contains("Question two", input);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private static ChatOwner Owner(WorkItem item)
    {
        return ChatOwner.ForWorkItem(Repo, item.Id);
    }

    private async Task<WorkItem> PrepareEpicAsync()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        return await Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Epic", Description = "D" }, _cancellationToken);
    }

    private IRenderedComponent<WorkItemEditor> RenderEpic(WorkItem epic)
    {
        Navigation.NavigateTo($"/epics/{epic.Id}");
        if (!_popoverRendered)
        {
            Render<MudPopoverProvider>();
            _popoverRendered = true;
        }

        return Render<WorkItemEditor>(p => p.Add(e => e.Id, epic.Id));
    }

    private static async Task SendAsync(IRenderedComponent<WorkItemEditor> cut, string text)
    {
        var input = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Describe what you want to build").Find("input");
        input.Input(text);
        await cut.InvokeAsync(() => input.KeyDown("Enter"));
    }

    private async Task WaitForStoredAsync(WorkItem epic, int displayed)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await Store.GetDisplayMessagesAsync(Owner(epic), _cancellationToken)).Count >= displayed)
            {
                return;
            }
            await Task.Delay(50, _cancellationToken);
        }

        Assert.Fail("The conversation was not stored.");
    }

    private Task SelectRepositoryAsync()
    {
        return Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", Repo, new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null));
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
